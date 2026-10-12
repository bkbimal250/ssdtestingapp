using System.Diagnostics;
using System.IO;
using System.Text.Json;
using pixinit.Core.Benchmark;
using pixinit.Infrastructure.Benchmarking;
using pixinit.Application.Reporting;
using pixinit.ViewModels.Benchmark;

namespace pixinit.Tests;
internal static partial class Program
{
    private static async Task UnbufferedBenchmarkTests()
    {
        var c = BenchmarkPolicy.Quick with { Unbuffered = true, FileSizeBytes = BenchmarkPolicy.MiB, Iterations = 1, RandomOperationCount = 512, Operations = BenchmarkOperation.SequentialRead | BenchmarkOperation.RandomRead };
        DirectBenchmarkPolicy.Validate(c, 20L * 1073741824);
        Check((NativeBenchmarkFile.Flags & 0xE0000000) == 0xE0000000 && !FileBenchmarkEngine.UsesRawDeviceWrites, "Native mode requests no-buffering, write-through and overlapped file I/O without raw writes");
        var large = c with { FileSizeBytes = 4L * 1073741824, Iterations = 9, RandomOperationCount = 65536, Operations = BenchmarkPolicy.Quick.Operations };
        DirectBenchmarkPolicy.Validate(large, 20L * 1073741824);
        Check(large.MaximumWriteBytes() == 4L * 1073741824 + 2L * 4 * 1073741824 * 9 + 2L * 4096 * 65536 * 9, "Four GiB, nine passes budgets both sequential/random profiles and preparation before consent");
        bool refused = false; try { DirectBenchmarkPolicy.Validate(c with { Iterations = 2 }, long.MaxValue); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Unbuffered unsupported pass count is rejected");
        string root = Path.Combine(Path.GetTempPath(), "pixinit-direct-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var target = new BenchmarkTarget(root, Path.GetPathRoot(root)!, "NTFS", new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace, null, "Disposable synthetic target", false);
            var engine = new FileBenchmarkEngine();
            refused = false; try { await engine.RunAsync(target, c, false); } catch (InvalidOperationException) { refused = true; }
            Check(refused && !Directory.Exists(Path.Combine(root, OwnedBenchmarkFileManager.DirectoryName)), "Unbuffered consent denial creates no owned file");
            var session = await engine.RunAsync(target, c, true);
            var failureDisplay = pixinit.ViewModels.Benchmark.BenchmarkViewModel.DescribeSessionProblem(session with { Completion = BenchmarkCompletion.Failed, Reason = "Application-write ceiling reached before timed window ended" });
            Check(failureDisplay.Contains("ceiling") && failureDisplay.Contains("cleaned") && failureDisplay.Contains("not space still occupied"), "Failed session exposes exact reason and separates cumulative writes from occupied space");
            var cleanupDisplay = pixinit.ViewModels.Benchmark.BenchmarkViewModel.DescribeSessionProblem(session with { CleanupSucceeded = false, BenchmarkFilePath = "test-owned-file" });
            Check(cleanupDisplay.Contains("test-owned-file") && cleanupDisplay.Contains("Retry"), "Cleanup failure exposes owned path and recovery action instead of a success claim");
            Check(session.Completion == BenchmarkCompletion.Completed && session.PassResults.Count == 4 && session.CleanupSucceeded && session.PreparationBytesWritten == BenchmarkPolicy.MiB, "All four native queue profiles complete on a disposable one MiB prepared file: " + session.Reason);
            Check(session.PassResults.All(p => p.Measurement.State == BenchmarkOperationState.Completed && p.Measurement.MeasuredSeconds > 0) && session.PassResults.Single(p => p.Profile == "RND4K Q32T16").Workers == 16, "Native worker queues retain requested concurrency and positive timing");
            var writes = c with { Profiles = BenchmarkProfiles.Rnd4KQ1T1 | BenchmarkProfiles.Rnd4KQ32T16, Operations = BenchmarkOperation.RandomWrite, RandomOperationCount = 32 };
            var written = await engine.RunAsync(target, writes, true);
            Check(written.Completion == BenchmarkCompletion.Completed && written.PreparationBytesWritten == 0 && written.PassResults.Sum(p => p.Measurement.BytesProcessed) == 2 * 32 * 4096 && written.CleanupSucceeded, "Native random writes stay within a 256 KiB application budget and clean up");
            using var cancel = new CancellationTokenSource();
            var cancelled = await engine.RunAsync(target, c, true, new InlineProgress(p => { if(p.Operation is not null) cancel.Cancel(); }), cancel.Token);
            Check(cancelled.Completion == BenchmarkCompletion.Cancelled && cancelled.CleanupSucceeded, "Cancellation drains native requests before owned-file cleanup");
            var manager = new OwnedBenchmarkFileManager(); var owned = await manager.CreateAsync(root, Guid.NewGuid(), default);
            refused = false; try { using var file = new NativeBenchmarkFile(owned with { FileIndex = owned.FileIndex + 1 }, BenchmarkPolicy.MiB); } catch (IOException) { refused = true; }
            Check(refused, "Native handle rejects replacement identity before resizing or I/O"); await manager.CleanupAsync(owned);
            var m1 = BenchmarkOperationResult.Calculate(BenchmarkOperation.SequentialRead, 100000000, 100, Stopwatch.Frequency, null);
            var m2 = BenchmarkOperationResult.Calculate(BenchmarkOperation.SequentialRead, 300000000, 300, Stopwatch.Frequency, null);
            var average = DirectBenchmarkPolicy.Summarize([new("SEQ1M Q1T1", 1, 1, 1, m1), new("SEQ1M Q1T1", 2, 1, 1, m2), new("SEQ1M Q1T1", 3, 1, 1, m2 with { State = BenchmarkOperationState.Cancelled })]).Single();
            Check(average.AverageMBs == 200 && average.BestMBs == 300 && average.CompletedPasses == 2, "Arithmetic pass average and best exclude incomplete measurements");
            var sata = target with { PhysicalIdentityReliable = true, Device = pixinit.Core.History.SnapshotFactory.DeviceSnapshot(SyntheticNvme()) with { Protocol = "Sata" } };
            string warning = DirectBenchmarkPolicy.Plausibility(sata, [m1 with { MegabytesPerSecond = 4470 }]);
            Check(warning.StartsWith("INVALID") && DirectBenchmarkPolicy.Plausibility(sata, [m1]).StartsWith("No model"), "Implausible SATA result is invalidated without manufacturing a capped speed");
            var saved = JsonSerializer.Deserialize<BenchmarkSession>(JsonSerializer.Serialize(session))!;
            Check(saved.PassResults.Count == 4 && saved.Configuration.Unbuffered && saved.Methodology.Contains("storage alignment"), "In-memory result retains native flags/alignment and every profile pass without persistence");
            string report = Path.Combine(root, "direct.json"); await BenchmarkReportExporter.ExportJsonAsync(session, report);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
            Check(json.RootElement.GetProperty("Session").GetProperty("PassResults").GetArrayLength() == 4 && (await File.ReadAllTextAsync(report)).Contains("Unbuffered"), "JSON exports native mode and per-pass measurements");
            Check(BenchmarkComparisons.Compare(session, session with { StartedUtc = session.StartedUtc.AddSeconds(1), Configuration = c with { Unbuffered = false } }).Count == 0, "Buffered and unbuffered sessions are not compared");
            var vm = new BenchmarkViewModel(engine, () => [], () => null, () => { });
            Check(vm.UseUnbuffered && vm.Iterations == 1 && vm.FileSizes.SequenceEqual([512,1024,2048]) && vm.TargetDirectory.Length == 0, "UI defaults to one bounded unbuffered pass with segmented sizes and no selected target");
        }
        finally { Directory.Delete(root, true); }
    }
}