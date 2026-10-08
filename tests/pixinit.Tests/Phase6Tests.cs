using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using pixinit.Application.Benchmarking;
using pixinit.Application.Reporting;
using pixinit.Core.Benchmark;
using pixinit.Core.History;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Infrastructure.Benchmarking;
using pixinit.Infrastructure.History;
using pixinit.Infrastructure.Windows.Nvme;
using pixinit.Infrastructure.Windows.Sata;
using pixinit.ViewModels.Benchmark;

namespace pixinit.Tests;

internal static partial class Program
{
    private sealed class InlineProgress(Action<BenchmarkProgress> action) : IProgress<BenchmarkProgress> { public void Report(BenchmarkProgress value) => action(value); }
    private sealed class Consent(bool answer) : IWriteConsent { public int Calls; public Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes) { Calls++; return Task.FromResult(answer); } }

    private static async Task Phase6Tests()
    {
        long ticks = Stopwatch.Frequency * 2;
        var seq = BenchmarkOperationResult.Calculate(BenchmarkOperation.SequentialRead, 20_000_000, 20, ticks, new(1, .5, 2, 20));
        Check(seq.BytesPerSecond == 10_000_000 && seq.MegabytesPerSecond == 10 && Math.Abs(seq.MebibytesPerSecond - 9.5367431640625) < 1e-10, "Sequential throughput keeps decimal MB/s distinct from binary MiB/s");
        var random = BenchmarkOperationResult.Calculate(BenchmarkOperation.RandomRead, 4096 * 2000L, 2000, ticks, new(1, .25, 3, 2000));
        Check(random.Iops == 1000 && random.Latency is { AverageMilliseconds: 1, MinimumMilliseconds: .25, MaximumMilliseconds: 3, Samples: 2000 }, "Random IOPS and latency derive from completed operations and monotonic measured time");
        Check(Stopwatch.IsHighResolution && Stopwatch.GetTimestamp() > 0, "Benchmark duration source is the high-resolution monotonic Stopwatch clock");
        Check(BenchmarkPolicy.Quick.FileSizeBytes == 32 * BenchmarkPolicy.MiB && BenchmarkPolicy.Standard.FileSizeBytes == 128 * BenchmarkPolicy.MiB, "Quick and Standard presets have fixed documented file sizes");
        Check(BenchmarkPolicy.Quick.MaximumWriteBytes() == 64 * BenchmarkPolicy.MiB + 1000L * 4096 && BenchmarkPolicy.Standard.MaximumWriteBytes() == 448 * BenchmarkPolicy.MiB, "Preset maximum write amounts are known before execution");
        bool rejected = false; try { BenchmarkPolicy.Validate(BenchmarkPolicy.Quick with { FileSizeBytes = 1025 * BenchmarkPolicy.MiB }, long.MaxValue); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Custom test files above 1024 MiB are rejected");
        rejected = false; try { BenchmarkPolicy.Validate(BenchmarkPolicy.Standard with { FileSizeBytes = 1024 * BenchmarkPolicy.MiB }, long.MaxValue); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Custom workloads above the 3 GiB write allowance are rejected");
        Check(BenchmarkPolicy.SafetyMargin(5_000_000_000) == BenchmarkPolicy.MinimumSafetyMarginBytes && BenchmarkPolicy.SafetyMargin(40_000_000_000) == 4_000_000_000 && BenchmarkPolicy.SafetyMargin(5_000_000_000, sustainedOnly: true) == 1_073_741_824, "Ordinary reserve is at least 3 GiB or ten percent; sustained-only retains one GiB reserve");
        rejected = false; try { BenchmarkPolicy.Validate(BenchmarkPolicy.Quick, BenchmarkPolicy.Quick.FileSizeBytes + BenchmarkPolicy.MinimumSafetyMarginBytes - 1); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Insufficient free space is rejected before file creation");

        string root = Path.Combine(Path.GetTempPath(), "pixinit-phase6-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var target = new BenchmarkTarget(root, Path.GetPathRoot(root)!, new DriveInfo(root).DriveFormat, new DriveInfo(root).AvailableFreeSpace, null, "Synthetic test target; identity uncertain", false);
            var manager = new OwnedBenchmarkFileManager(); var engine = new FileBenchmarkEngine(manager);
            var config = new BenchmarkConfiguration(BenchmarkPreset.Custom, BenchmarkOperation.SequentialRead | BenchmarkOperation.SequentialWrite | BenchmarkOperation.RandomRead | BenchmarkOperation.RandomWrite, 16 * BenchmarkPolicy.MiB, 1024 * 1024, 4096, 1, 256, 1);
            rejected = false; try { await engine.RunAsync(target, config, false); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !Directory.Exists(Path.Combine(root, OwnedBenchmarkFileManager.DirectoryName)), "Preparation and write operations require explicit consent before owned-file creation");

            var prepared = await manager.CreateAsync(root, Guid.NewGuid(), default); await using (var f = File.Create(prepared.FilePath)) f.SetLength(config.FileSizeBytes);
            var readOnly = config with { Operations = BenchmarkOperation.SequentialRead };
            var readSession = await engine.RunAsync(target, readOnly, false, preparedFile: prepared);
            Check(readSession.Completion == BenchmarkCompletion.Completed && !readSession.WriteConsentRequired && readSession.PreparationBytesWritten == 0, "Read-only benchmark uses a valid existing owned file without write consent or preparation");
            Check(readSession.CleanupSucceeded && !File.Exists(prepared.FilePath), "Owned benchmark file is removed after normal completion");

            long ataBefore = WindowsAtaTransport.DispatchCount, nvmeBefore = WindowsNvmeTransport.DispatchCount;
            var session = await engine.RunAsync(target, config, true);
            Check(session.Completion == BenchmarkCompletion.Completed && session.Results.Select(r => r.Operation).SequenceEqual([BenchmarkOperation.SequentialWrite, BenchmarkOperation.SequentialRead, BenchmarkOperation.RandomWrite, BenchmarkOperation.RandomRead]), "Combined benchmark runs four separately reported operations in documented order");
            Check(session.Results.All(r => r.State == BenchmarkOperationState.Completed && r.BytesProcessed > 0 && r.MeasuredSeconds > 0) && session.Results.Where(r => r.Iops is not null).All(r => r.CompletedOperations == 256), "Sequential and random operations retain bytes, duration, throughput, IOPS and latency");
            Check(session.PreparationBytesWritten + session.Results.Where(r => r.Operation is BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite).Sum(r => r.BytesProcessed) <= config.MaximumWriteBytes(), "Actual filesystem writes stay within the precomputed workload allowance");
            Check(session.CleanupSucceeded && !File.Exists(session.BenchmarkFilePath), "Normal benchmark cleanup removes only its proven owned file");
            Check(!FileBenchmarkEngine.UsesRawDeviceWrites && WindowsAtaTransport.DispatchCount == ataBefore && WindowsNvmeTransport.DispatchCount == nvmeBefore, "Benchmark uses no raw-drive path and dispatches no ATA or NVMe commands");

            using var cancel = new CancellationTokenSource(); var cancelled = await engine.RunAsync(target, config, true, new InlineProgress(p => { if (p.Operation == BenchmarkOperation.SequentialWrite && p.BytesProcessed >= 2 * BenchmarkPolicy.MiB) cancel.Cancel(); }), cancel.Token);
            Check(cancelled.Completion == BenchmarkCompletion.Cancelled && cancelled.Results.Any(r => r.State == BenchmarkOperationState.Cancelled), "Cancellation stops scheduling and preserves an explicitly incomplete operation");
            Check(cancelled.CleanupSucceeded && !File.Exists(cancelled.BenchmarkFilePath), "Cancellation closes handles and cleans the owned benchmark file");

            engine.FailOperationForTest = BenchmarkOperation.SequentialRead; var failedSession = await engine.RunAsync(target, readOnly, true); engine.FailOperationForTest = null;
            Check(failedSession.Completion == BenchmarkCompletion.Failed && failedSession.Results.Single().State == BenchmarkOperationState.Failed && failedSession.CleanupSucceeded, "Operation failure is recorded as Failed while owned-file cleanup still completes");

            manager.BeforeDeleteForTest = _ => throw new IOException("Synthetic cleanup denial"); var cleanupFailed = await engine.RunAsync(target, readOnly, true);
            Check(!cleanupFailed.CleanupSucceeded && cleanupFailed.Reason!.Contains("remains at") && File.Exists(cleanupFailed.BenchmarkFilePath), "Cleanup failure is visible and preserves the exact owned-file location");
            manager.BeforeDeleteForTest = null; var orphan = (await manager.FindOrphansAsync(root)).Single(r => r.FilePath == cleanupFailed.BenchmarkFilePath);
            var recoveryVm = new BenchmarkViewModel(engine, null, () => [], () => null, () => { }) { TargetDirectory = root }; await recoveryVm.InitializeAsync();
            Check(recoveryVm.OwnedFilesNeedingCleanup.Single().FilePath == orphan.FilePath && recoveryVm.RetryCleanupCommand.CanExecute(null), "Restart surfaces the exact verified owned file and enables safe cleanup retry");
            recoveryVm.RetryCleanupCommand.Execute(null); for (int i = 0; i < 100 && !recoveryVm.Status.StartsWith("Cleaned ", StringComparison.Ordinal); i++) await Task.Delay(10);
            Check(!File.Exists(orphan.FilePath), "Interrupted owned-file manifest supports safe restart discovery and retry cleanup");
            string unrelated = Path.Combine(root, "unrelated.bin"); await File.WriteAllTextAsync(unrelated, "keep"); string ownedDirectory = Path.Combine(root, OwnedBenchmarkFileManager.DirectoryName); Directory.CreateDirectory(ownedDirectory);
            var redirected = new OwnedFileRecord(Guid.NewGuid(), "BAD", unrelated, unrelated + ".owner.json", DateTimeOffset.UtcNow, 0, 0); await File.WriteAllTextAsync(Path.Combine(ownedDirectory, "redirect.owner.json"), JsonSerializer.Serialize(redirected));
            Check((await manager.FindOrphansAsync(root)).Count == 0 && File.Exists(unrelated), "Untrusted manifest paths cannot redirect cleanup outside the owned benchmark directory");

            string db = Path.Combine(root, "history.db"); var store = new SqliteHistoryStore(db); await store.InitializeAsync();
            var diagnostic = SnapshotFactory.Create(Guid.NewGuid(), SyntheticNvme(), AssessedNvme(NvmeHealthParser.Parse(HealthyPhase6Health()))); await store.SaveAsync(diagnostic); await store.SaveBenchmarkAsync(session);
            var restarted = new SqliteHistoryStore(db); await restarted.InitializeAsync(); var reopened = await restarted.LoadBenchmarkAsync(session.Id);
            Check(reopened?.Results.Count == 4 && await restarted.LoadAsync(diagnostic.Id) is not null, "Schema v2 restart reopens benchmark history and preserves diagnostic history");
            Check(reopened!.Results.Select(r => r.BytesProcessed).SequenceEqual(session.Results.Select(r => r.BytesProcessed)), "Benchmark history preserves counters and measured values losslessly");
            var rollback = new SqliteHistoryStore(Path.Combine(root, "rollback.db")); await rollback.InitializeAsync(); rollback.FailAfterBenchmarkInsertForTest = true; rejected = false; try { await rollback.SaveBenchmarkAsync(session with { Id = Guid.NewGuid() }); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && await rollback.CountBenchmarksAsync() == 0, "Benchmark history transaction rolls back on forced failure");
            string migrationDb = Path.Combine(root, "migration.db"); await using (var c = new SqliteConnection($"Data Source={migrationDb}")) { await c.OpenAsync(); await new SqliteCommand("CREATE TABLE sessions(id TEXT PRIMARY KEY); INSERT INTO sessions VALUES('preserve-me'); PRAGMA user_version=1;", c).ExecuteNonQueryAsync(); }
            await new SqliteHistoryStore(migrationDb).InitializeAsync(); await using (var c = new SqliteConnection($"Data Source={migrationDb}")) { await c.OpenAsync(); long preserved = Convert.ToInt64(await new SqliteCommand("SELECT COUNT(*) FROM sessions WHERE id='preserve-me';", c).ExecuteScalarAsync()); long version = Convert.ToInt64(await new SqliteCommand("PRAGMA user_version;", c).ExecuteScalarAsync()); Check(preserved == 1 && version == 2, "Transactional v1-to-v2 migration preserves existing diagnostic rows"); }

            var reliableDevice = SnapshotFactory.DeviceSnapshot(SyntheticNvme()); var mappedTarget = target with { Device = reliableDevice, PhysicalIdentityReliable = true };
            var older = session with { Id = Guid.NewGuid(), StartedUtc = session.StartedUtc.AddMinutes(-1), FinishedUtc = session.FinishedUtc.AddMinutes(-1), Target = mappedTarget };
            var newer = session with { Id = Guid.NewGuid(), Target = mappedTarget, Results = session.Results.Select(r => r with { MegabytesPerSecond = r.MegabytesPerSecond + 5 }).ToArray() };
            Check(BenchmarkComparisons.Compare(older, newer).Count == 4, "Compatible same-target methodology yields absolute and percentage comparison deltas");
            Check(BenchmarkComparisons.Compare(older, newer with { Configuration = newer.Configuration with { RandomBlockBytes = 8192 } }).Count == 0, "Methodology changes reject historical comparison");
            Check(BenchmarkComparisons.Compare(older with { Target = target }, newer with { Target = target }).Count == 0, "Ambiguous physical identity disables automatic benchmark comparison");

            string json = Path.Combine(root, "benchmark.json"), txt = Path.Combine(root, "benchmark.txt"); await BenchmarkReportExporter.ExportJsonAsync(newer, json); await BenchmarkReportExporter.ExportTextAsync(newer, txt);
            string jsonText = await File.ReadAllTextAsync(json), txtText = await File.ReadAllTextAsync(txt); using var doc = JsonDocument.Parse(jsonText);
            Check(doc.RootElement.GetProperty("Session").GetProperty("Target").GetProperty("Device").GetProperty("Serial").GetString() == "REDACTED" && doc.RootElement.GetProperty("LosslessIntegerValues").GetProperty("fileSizeBytes").GetString() == config.FileSizeBytes.ToString(), "Benchmark JSON redacts serial and documents lossless integer strings");
            Check(txtText.Contains("MB/s = bytes/second / 1,000,000") && txtText.Contains(BenchmarkPolicy.Version) && txtText.Contains("Temperature before:") && !txtText.Contains("benchmark test data"), "Benchmark TXT includes policy, units, temperature context, methodology and no test-data contents");

            var consent = new Consent(true); var vm = new BenchmarkViewModel(engine, null, () => [], () => null, () => { }, consent) { TargetDirectory = root, Preset = BenchmarkPreset.Custom, FileSizeMiB = 16, SequentialWrite = false, RandomWrite = false, RandomRead = false };
            await vm.RunAsync(); vm.FileSizeMiB = 32; vm.ConfigurationChanged(); await vm.RunAsync();
            Check(consent.Calls == 2 && vm.ConsentStatus.Contains("granted"), "Consent is explicit and invalidated after a material configuration change");
            var declinedConsent = new Consent(false); var declinedVm = new BenchmarkViewModel(engine, null, () => [], () => null, () => { }, declinedConsent) { TargetDirectory = root, Preset = BenchmarkPreset.Custom, FileSizeMiB = 16, SequentialWrite = false, RandomWrite = false, RandomRead = false }; await declinedVm.RunAsync();
            Check(declinedConsent.Calls == 1 && declinedVm.ResultSummary == "No benchmark result", "Declined write preparation consent performs no benchmark");

            var badStore = new SqliteHistoryStore(root); var persistenceVm = new BenchmarkViewModel(engine, badStore, () => [], () => null, () => { }, new Consent(true)) { TargetDirectory = root, Preset = BenchmarkPreset.Custom, FileSizeMiB = 16, SequentialWrite = false, RandomWrite = false, RandomRead = false }; await persistenceVm.RunAsync();
            Check(persistenceVm.Result is { Completion: BenchmarkCompletion.Completed } && persistenceVm.Status.Contains("history save failed"), "Benchmark remains visible when history persistence fails");

            var staleVm = new BenchmarkViewModel(engine, null, () => [], () => null, () => { }, new Consent(true)) { TargetDirectory = root, Preset = BenchmarkPreset.Custom, FileSizeMiB = 16, SequentialWrite = false, RandomWrite = false, RandomRead = false };
            bool switched = false; staleVm.PropertyChanged += (_, e) => { if (!switched && e.PropertyName == nameof(staleVm.ProgressPercent) && staleVm.ProgressPercent > 0) { switched = true; staleVm.SelectionChanged(); } };
            await staleVm.RunAsync(); Check(switched && staleVm.Result is null && staleVm.Status.Contains("discarded") && (await manager.FindOrphansAsync(root)).Count == 0, "Device-selection change cancels, cleans and rejects the stale benchmark result");

            var closeVm = new BenchmarkViewModel(engine, null, () => [], () => null, () => { }, new Consent(true)) { TargetDirectory = root, Preset = BenchmarkPreset.Custom, FileSizeMiB = 16, SequentialWrite = false, RandomWrite = false, RandomRead = false };
            bool closed = false; closeVm.PropertyChanged += (_, e) => { if (!closed && e.PropertyName == nameof(closeVm.ProgressPercent) && closeVm.ProgressPercent > 0) { closed = true; closeVm.Close(); } };
            await closeVm.RunAsync(); await closeVm.CancelAndWaitAsync(); Check(closed && closeVm.Result is null && (await manager.FindOrphansAsync(root)).Count == 0, "Application close waits for cancellation and owned-file cleanup before discarding the result");
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static byte[] HealthyPhase6Health() { var b = SyntheticHealth(); b[0] = 0; b[5] = 50; Array.Clear(b, 160, 16); return b; }
}
