using System.IO;
using System.Text.Json;
using pixinit.Application.Assessment;
using pixinit.Application.Reporting;
using pixinit.Application.Scanning;
using pixinit.Core.Benchmark;
using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Benchmarking;
using pixinit.Infrastructure.History;
using pixinit.ViewModels.Benchmark;

namespace pixinit.Tests;
internal static partial class Program
{
    private sealed class ChangeSpecsConsent(Action change) : pixinit.Application.Benchmarking.IWriteConsent
    {
        public Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes) { change(); return Task.FromResult(true); }
    }
    private static async Task StorageSpecsTests()
    {
        Check(HealthAssessor.TemperatureState(74.9) == ThermalState.Green && HealthAssessor.TemperatureState(75) == ThermalState.Yellow && HealthAssessor.TemperatureState(80) == ThermalState.Yellow && HealthAssessor.TemperatureState(80.1) == ThermalState.Red && HealthAssessor.TemperatureState(null) == ThermalState.Unknown, "Temperature assessment preserves requested strict boundaries and unavailable state");
        Check(new TbwInfo(540).RemainingPercent == 10 && HealthAssessor.EnduranceState(new TbwInfo(541)).Contains("Warning") && new TbwInfo(null).RemainingPercent is null && new TbwInfo(800).RemainingPercent == 0, "Reference TBW remaining is bounded, unavailable-aware and warns below ten percent");
        Check(HealthAssessor.EnduranceState(new TbwInfo(590, InterpretationVerified: false)) == "Unverified SMART interpretation", "Unverified SATA units cannot produce a confirmed endurance warning");
        var largeRead = BenchmarkPolicy.Quick with { FileSizeBytes = 1024 * BenchmarkPolicy.MiB, Operations = BenchmarkOperation.SequentialRead };
        BenchmarkPolicy.Validate(largeRead, 20 * 1024 * BenchmarkPolicy.MiB);
        Check(largeRead.MaximumWriteBytes() == 1_073_741_824 && BenchmarkPolicy.MaxOrdinaryApplicationDataWritesBytes == 3_221_225_472, "1024 MiB read preparation fits the three GiB ordinary application write cap");
        var largeCombined = BenchmarkPolicy.Quick with { FileSizeBytes = 1024 * BenchmarkPolicy.MiB };
        BenchmarkPolicy.Validate(largeCombined, 20 * 1024 * BenchmarkPolicy.MiB);
        Check(largeCombined.MaximumWriteBytes() == 2_151_579_648 && largeCombined.MaximumWriteBytes() < BenchmarkPolicy.MaxOrdinaryApplicationDataWritesBytes, "1024 MiB sequential read/write and 1000 random read/write operations pass without actual large writes");
        var h = NvmeHealthParser.Parse(SyntheticHealth());
        var mapped = NvmeResultMapper.Map(SyntheticNvme(), null, h, null, [new(NvmeOperation.Health, NvmeOutcome.Success, "test", SyntheticHealth(), DateTimeOffset.UtcNow, "controller scope uncertain")]);
        Check(Math.Abs(mapped.Tbw!.WrittenTB!.Value - 3 * 512 * 1000d / 1e12) < 1e-12 && mapped.PowerOnHours == 8 && !mapped.Tbw.RatingVerified && mapped.PercentageUsed.Value == 123, "NVMe conversion preserves decimal host writes, scope, reference status and consumed endurance");
        Check(mapped.Thermal is { IdleC: null, LoadC: null, ObservedC: not null }, "Composite temperature is an observation without invented idle or load conditions");
        var command = new AtaCommandResult(AtaOperation.SmartData, AtaOutcome.Success, "test", new byte[512], new byte[8], DateTimeOffset.UtcNow);
        SataSmartAttribute Row(byte id, ulong raw) => new(id, "test", null, null, null, "test", "test", "test") { RawBytes = Enumerable.Range(0, 6).Select(i => (byte)(raw >> (i * 8))).ToArray() };
        var sata = SataResultMapper.Map(Drive("sata-spec", pixinit.Core.Devices.StorageProtocol.Sata), null, new([Row(194, 76), Row(241, 1_000_000), Row(9, 20)], []), null, [command], []);
        Check(sata.Temperature.Value == 76 && sata.PowerOnHours == 20 && sata.Tbw is { InterpretationVerified: false } && sata.Tbw.WrittenTB == 1_000_000 * 512d / 1e12, "SATA conventional 194/241/9 conversions retain their unverified vendor encoding disclosure");
        var duplicate = SataResultMapper.Map(Drive("sata-spec", pixinit.Core.Devices.StorageProtocol.Sata), null, new([Row(194, 76), Row(194, 77), Row(241, ulong.MaxValue)], []), null, [command], []);
        Check(duplicate.Temperature.Value is null && duplicate.Tbw!.WrittenTB is null, "Duplicate and all-FF SATA fields are not interpreted");
        SpeedSample Sample(int sec, long bytes, double interval = 1) => new(sec, bytes / interval / 1e6) { Bytes = bytes, IntervalSeconds = interval };
        var samples = new[] { Sample(1, 100_000_000), Sample(2, 100_000_000), Sample(3, 100_000_000), Sample(4, 40_000_000), Sample(5, 50_000_000), Sample(6, 60_000_000, 1.2) };
        var drop = FileBenchmarkEngine.AnalyzeSustained(samples);
        Check(drop.Second == 4 && Math.Abs(drop.MBs!.Value - 150 / 3.2) < 1e-8, "Sustained candidate drop uses three intervals and a byte/time weighted average");
        Check(FileBenchmarkEngine.AnalyzeSustained(samples.Take(5).ToArray()).Second is null && FileBenchmarkEngine.AnalyzeSustained(samples.Select((s, i) => Sample(i + 1, 100_000_000)).ToArray()).MBs is null, "Short or stable samples do not fabricate post-drop speed");
        var config = BenchmarkPolicy.Quick with { Operations = BenchmarkOperation.SustainedWrite, SustainedMaximumWriteBytes = BenchmarkPolicy.MiB, SustainedDurationSeconds = 1 };
        BenchmarkPolicy.Validate(config, 8L * 1_073_741_824);
        Check(config.HasWriteWork && config.MaximumWriteBytes() == BenchmarkPolicy.MiB && config.RequiredFileBytes == 1_073_741_824 && BenchmarkPolicy.Quick.SustainedMaximumWriteBytes == 32L * 1_073_741_824, "Sustained circular-file footprint and application write budget are independent");
        bool rejected = false; try { BenchmarkPolicy.Validate(config with { SustainedMaximumWriteBytes = 33L * 1_073_741_824 }, long.MaxValue); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && new BenchmarkProgress("test", BenchmarkOperation.SustainedWrite, 0, 0, 0).Phase == "SUSTAINED" && new BenchmarkProgress("test", BenchmarkOperation.RandomRead, 0, 0, 0).Phase == "RND", "Sustained budget ceiling and progress phases are explicit");
        string root = Path.Combine(Path.GetTempPath(), "pixinit-spec-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var target = new BenchmarkTarget(root, Path.GetPathRoot(root)!, new DriveInfo(root).DriveFormat, new DriveInfo(root).AvailableFreeSpace, null, "Disposable test directory, no hardware attribution", false);
            var engine = new FileBenchmarkEngine();
            rejected = false; try { await engine.RunAsync(target, config, false); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !Directory.Exists(Path.Combine(root, OwnedBenchmarkFileManager.DirectoryName)), "Sustained write requires consent before file creation");
            var session = await engine.RunAsync(target, config, true);
            Check(session.Completion == BenchmarkCompletion.Completed && session.Results.Single().BytesProcessed == BenchmarkPolicy.MiB && session.SustainedSamples.Count == 1 && session.SustainedPostDropMBs is null && session.SustainedOverallMBs > 0 && session.SustainedOverallMBs == session.Results.Single().BytesProcessed / session.Results.Single().MeasuredSeconds / 1e6 && session.SustainedStatus.Contains("No qualifying drop - showing overall average") && session.CleanupSucceeded, "Disposable one-MiB sustained test respects its byte cap, captures interval and cleans owned file");
            using (var stop = new CancellationTokenSource())
            {
                var stopped = await engine.RunAsync(target, config, true, new InlineProgress(p => { if (p.Operation == BenchmarkOperation.SustainedWrite && p.BytesProcessed >= BenchmarkPolicy.MiB) stop.Cancel(); }), stop.Token);
                Check(stopped.Completion == BenchmarkCompletion.Cancelled && stopped.SustainedPostDropMBs is null && stopped.SustainedOverallMBs > 0 && stopped.Results.Single().BytesProcessed <= config.MaximumWriteBytes() && stopped.CleanupSucceeded, "Sustained mid-run cancellation preserves bounded partial data and cleans owned file");
            }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            rejected = false; try { await engine.RunAsync(target, config, true, token: cancel.Token); } catch (OperationCanceledException) { rejected = true; }
            Check(rejected, "Pre-cancelled sustained request creates no workload");
            var latency = new BenchmarkLatency(2, 1, 3, 1000);
            var random = BenchmarkOperationResult.Calculate(BenchmarkOperation.RandomRead, 1000L * 4096, 1000, 3 * System.Diagnostics.Stopwatch.Frequency, latency);
            session = session with { Configuration = config with { Operations = BenchmarkOperation.RandomRead }, Results = [random], Tbw = mapped.Tbw, Thermal = mapped.Thermal };
            Check(session.RndReadIOPS == 500 && session.RndReadMBs == 500 * 4096d / 1_048_576 && Math.Abs(random.Iops!.Value - 1000 / 3d) < 1e-8, "Latency-derived random rate remains separate from end-to-end wall-clock IOPS");
            var store = new SqliteHistoryStore(Path.Combine(root, "history.db")); await store.InitializeAsync(); await store.SaveBenchmarkAsync(session);
            var loaded = await store.LoadBenchmarkAsync(session.Id);
            Check(loaded?.SustainedSamples.Count == 1 && loaded.Tbw?.WrittenTB == mapped.Tbw.WrittenTB && loaded.Thermal?.ObservedC == mapped.Thermal!.ObservedC && loaded.RndReadIOPS == 500 && loaded.SustainedOverallMBs == session.SustainedOverallMBs, "Complete benchmark JSON round-trips samples, reference TBW, thermal context and random rates");
            string export = Path.Combine(root, "report.json"); await BenchmarkReportExporter.ExportJsonAsync(session, export); using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(export));
            Check(doc.RootElement.GetProperty("Session").GetProperty("SustainedSamples").GetArrayLength() == 1 && doc.RootElement.GetProperty("Session").GetProperty("Tbw").GetProperty("RatingVerified").GetBoolean() == false && doc.RootElement.GetProperty("Session").GetProperty("SustainedOverallMBs").GetDouble() > 0, "Export preserves samples and unverified rating status");
            var changedVm = new BenchmarkViewModel(engine, null, () => [], () => null, () => { }) { TargetDirectory = root, SequentialRead = false, SequentialWrite = false, RandomRead = false, RandomWrite = false, SustainedWrite = true, SustainedBudgetMiB = 1, SustainedDurationSeconds = 1 };
            changedVm.ConsentProvider = new ChangeSpecsConsent(() => changedVm.SustainedBudgetMiB = 2);
            await changedVm.RunAsync();
            Check(changedVm.Result is null && changedVm.Status.Contains("changed after consent") && changedVm.ConsentStatus.Contains("not granted"), "Sustained budget changes after consent refuse all writes");
            var gate = new StorageOperationGate(); using (gate.Enter())
            {
                var vm = new BenchmarkViewModel(engine, null, () => [], () => null, () => { }, new Consent(true), operationGate: gate) { TargetDirectory = root, SequentialRead = false, SequentialWrite = false, RandomRead = false, RandomWrite = false, SustainedWrite = true, SustainedBudgetMiB = 1, SustainedDurationSeconds = 1 };
                await vm.RunAsync(); Check(vm.Result is null && !vm.Busy && vm.Status.Contains("still finishing") && await VerifyDiagnosticPreparationAsync(engine, root), "Shared storage gate rejects benchmark overlap and releases UI busy state");
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
