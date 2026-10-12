using System.Diagnostics;
using System.IO;
using System.Text.Json;
using pixinit.Core.Benchmark;
using pixinit.Infrastructure.Benchmarking;
namespace pixinit.Tests;
internal static partial class Program
{
    private static async Task AccurateResultTests()
    {
        using (var buffer = new AlignedBuffer(4097, 4096))
            Check((ulong)buffer.Pointer.ToInt64() % 4096 == 0 && buffer.Size == 8192, "VirtualAlloc buffer and allocation length satisfy alignment");
        string root = Path.Combine(Path.GetTempPath(), "pixinit-timed-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var manager = new OwnedBenchmarkFileManager();
        try
        {
            var plan = BenchmarkFilePlan.Create();
            var owned = await manager.CreateAsync(root, plan.SessionId, CancellationToken.None, plan.Nonce);
            Check(Path.GetFileName(owned.FilePath) == plan.FileName, "Owned-file creation uses exact filename planned before consent");
            bool collision = false;
            try { await manager.CreateAsync(root, plan.SessionId, CancellationToken.None, plan.Nonce); } catch (IOException) { collision = true; }
            Check(collision && File.Exists(owned.FilePath) && await manager.IsOwnedAsync(owned), "Planned filename collision never deletes existing owned file");
            bool refused = false;
            try { using var invalid = new NativeBenchmarkFile(owned, 1048577); } catch (IOException) { refused = true; }
            Check(refused && new FileInfo(owned.FilePath).Length == 0, "Misaligned file size refused before sizing or writes");
            using (var file = new NativeBenchmarkFile(owned, 1048576))
            {
                long prepared = 0; file.Prepare(1048576, null, CancellationToken.None, n => prepared += n);
                var read = file.Measure(BenchmarkProfile.All[1], BenchmarkOperation.SequentialRead, 1048576, long.MaxValue / 1048576, null, CancellationToken.None, 5);
                Console.WriteLine($"Timed read actual: {read.MeasuredSeconds:F4}s; {read.BytesProcessed} bytes; sector {file.SectorSize}, alignment {file.Alignment}");
                Check(read.State == BenchmarkOperationState.Completed && read.MeasuredSeconds is >= 4.8 and <= 5.2 && prepared == 1048576, "Five-second timed read wraps within disposable 1 MiB file");
                Check(Math.Abs(read.MegabytesPerSecond - read.BytesProcessed / read.MeasuredSeconds / 1e6) < 0.000001 && Math.Abs(read.MebibytesPerSecond - read.BytesProcessed / read.MeasuredSeconds / 1048576) < 0.000001, "Timed rates retain decimal MB/s and binary MiB/s separately");
                var capped = file.Measure(BenchmarkProfile.All[1], BenchmarkOperation.SequentialWrite, 1048576, 1, null, CancellationToken.None, 5);
                Check(capped.State == BenchmarkOperationState.Failed && capped.BytesProcessed == 1048576 && capped.Error!.Contains("ceiling"), "Timed write stops at disposable 1 MiB cap and is incomplete");
            }
            await manager.CleanupAsync(owned);
            Check(!File.Exists(owned.FilePath), "Timed file cleans up after native requests close");
            var c = BenchmarkPolicy.Quick with { Unbuffered = true, TimedMode = true, FileSizeBytes = 1073741824, Iterations = 5 };
            Check(c.MaximumWriteBytes() <= DirectBenchmarkPolicy.MaximumApplicationWrites && c.Signature != (c with { TimedMode = false }).Signature && c.Signature != (c with { DurationSeconds = 10 }).Signature, "Timed ceiling includes preparation and consent signature includes timing");
            Check(Preconditioning.TrimStatus.StartsWith("Skipped") && Preconditioning.TrimStatus.Contains("retained"), "TRIM is explicitly logged as skipped for retained read-after-write data");
            var m = BenchmarkOperationResult.Calculate(BenchmarkOperation.SequentialRead, 100000000, 100, Stopwatch.Frequency, null);
            var summaries = DirectBenchmarkPolicy.Summarize([new("SEQ", 1, 1, 1, m), new("SEQ", 2, 1, 1, m with { MegabytesPerSecond = 300 }), new("SEQ", 3, 1, 1, m with { MegabytesPerSecond = 700 }) { IsValidForDeviceAttribution = false, IncludeInAverage = false }]);
            Check(summaries.Single().AverageMBs == 200 && summaries.Single().BestMBs == 300 && summaries.Single().CompletedPasses == 2, "Invalid-attribution measurements excluded from timed average and best");
            var json = JsonSerializer.Serialize(new BenchmarkPassResult("SEQ", 1, 1, 1, m) { IsValidForDeviceAttribution = false });
            Check(json.Contains("IsValidForDeviceAttribution") && json.Contains("ElapsedSeconds") && json.Contains("TotalBytes"), "Pass export retains attribution, exact elapsed time and byte count");
        }
        finally { Directory.Delete(root, true); }
    }
}

