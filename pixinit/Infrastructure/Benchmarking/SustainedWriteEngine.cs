using System.Diagnostics;
using System.IO;
using pixinit.Core.Benchmark;

namespace pixinit.Infrastructure.Benchmarking;

public sealed partial class FileBenchmarkEngine
{
    private sealed record SustainedMeasurement(BenchmarkOperationResult Result, List<SpeedSample> Samples, int? DropSecond, double? PostDropMBs, string Status);
    internal static (int? Second, double? MBs) AnalyzeSustained(IReadOnlyList<SpeedSample> samples)
    {
        var full = samples.Where(s => s.IntervalSeconds >= .9 && s.Bytes > 0 && double.IsFinite(s.mbs)).ToArray();
        if (full.Length < 6) return (null, null);
        double baseline = full.Take(3).Sum(s => s.Bytes) / full.Take(3).Sum(s => s.IntervalSeconds) / 1e6;
        if (baseline <= 0) return (null, null);
        for (int i = 3; i + 2 < full.Length; i++)
        {
            if (!full.Skip(i).Take(3).All(s => s.mbs < baseline * .7)) continue;
            var after = full.Skip(i).ToArray();
            return (full[i].sec, after.Sum(s => s.Bytes) / after.Sum(s => s.IntervalSeconds) / 1e6);
        }
        return (null, null);
    }
    private static async Task<SustainedMeasurement> MeasureSustainedAsync(string path, BenchmarkConfiguration c, IProgress<BenchmarkProgress>? progress, CancellationToken token)
    {
        long start = Stopwatch.GetTimestamp(), sampleStart = start, bytes = 0, sampleBytes = 0, operations = 0;
        long lastProgress = start;
        var samples = new List<SpeedSample>();
        var state = BenchmarkOperationState.Completed; string? error = null;
        void Sample()
        {
            double interval = Elapsed(sampleStart);
            if (sampleBytes == 0 || interval <= 0) return;
            var sample = new SpeedSample((int)Math.Round(Elapsed(start)), sampleBytes / interval / 1e6) { Bytes = sampleBytes, IntervalSeconds = interval };
            samples.Add(sample); sampleBytes = 0; sampleStart = Stopwatch.GetTimestamp();
            progress?.Report(new("SUSTAINED: buffered circular-file writes", BenchmarkOperation.SustainedWrite, bytes, c.SustainedMaximumWriteBytes, Elapsed(start)) { Sample = sample });
        }
        try
        {
            await using var stream = Open(path, FileMode.Open, FileAccess.ReadWrite, (int)BenchmarkPolicy.MiB, FileOptions.SequentialScan);
            if (stream.Length != c.RequiredFileBytes) throw new IOException("Sustained file length changed; write refused.");
            byte[] buffer = Pattern((int)BenchmarkPolicy.MiB);
            while (Elapsed(start) < c.SustainedDurationSeconds && bytes < c.SustainedMaximumWriteBytes)
            {
                token.ThrowIfCancellationRequested();
                if (stream.Position >= c.RequiredFileBytes) stream.Position = 0;
                int count = (int)Math.Min(buffer.Length, c.SustainedMaximumWriteBytes - bytes);
                await stream.WriteAsync(buffer.AsMemory(0, count), token); bytes += count; sampleBytes += count; operations++;
                if (Stopwatch.GetTimestamp() - lastProgress >= Stopwatch.Frequency / 10 || bytes >= c.SustainedMaximumWriteBytes)
                { progress?.Report(new("SUSTAINED: buffered circular-file writes", BenchmarkOperation.SustainedWrite, bytes, c.SustainedMaximumWriteBytes, Elapsed(start))); lastProgress = Stopwatch.GetTimestamp(); }
                if (Elapsed(sampleStart) >= 1) { await stream.FlushAsync(token); Sample(); }
            }
            await stream.FlushAsync(token); Sample();
        }
        catch (OperationCanceledException) { state = BenchmarkOperationState.Cancelled; error = "Sustained write cancelled; incomplete measurement."; Sample(); }
        catch (Exception ex) { state = BenchmarkOperationState.Failed; error = ex.Message; Sample(); }
        var result = BenchmarkOperationResult.Calculate(BenchmarkOperation.SustainedWrite, bytes, operations, Stopwatch.GetTimestamp() - start, null, state, error);
        var drop = AnalyzeSustained(samples);
        bool limited = bytes >= c.SustainedMaximumWriteBytes && result.MeasuredSeconds < c.SustainedDurationSeconds;
        string status = state != BenchmarkOperationState.Completed ? error ?? "Incomplete" : limited ? "Byte budget reached before requested duration" : "Requested time window completed";
        status += drop.Second is int second ? $"; observed throughput drop at {second}s (not proof of SLC exhaustion)" : "; No qualifying drop - showing overall average";
        return new(result, samples, drop.Second, state == BenchmarkOperationState.Completed ? drop.MBs : null, status);
    }
}
