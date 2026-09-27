using System.Diagnostics;
using System.IO;
using pixinit.Core.Benchmark;

namespace pixinit.Infrastructure.Benchmarking;

public sealed class FileBenchmarkEngine
{
    private readonly OwnedBenchmarkFileManager files;
    public FileBenchmarkEngine(OwnedBenchmarkFileManager? files = null) => this.files = files ?? new();
    public static bool UsesRawDeviceWrites => false;

    public async Task<BenchmarkSession> RunAsync(BenchmarkTarget target, BenchmarkConfiguration configuration, bool consentGranted,
        IProgress<BenchmarkProgress>? progress = null, CancellationToken token = default, OwnedFileRecord? preparedFile = null)
    {
        bool existing = preparedFile is not null && await files.IsOwnedAsync(preparedFile, token) && File.Exists(preparedFile.FilePath);
        BenchmarkPolicy.Validate(configuration, target.AvailableBytes, existing);
        bool consentRequired = configuration.HasWriteWork || (configuration.HasReadWork && !existing);
        if (consentRequired && !consentGranted) throw new InvalidOperationException("Explicit write consent is required for the selected workload or read-file preparation.");
        Guid id = Guid.NewGuid(); DateTimeOffset started = DateTimeOffset.UtcNow; long totalStart = Stopwatch.GetTimestamp();
        OwnedFileRecord owned = preparedFile ?? await files.CreateAsync(target.Directory, id, token); bool cleanup = false;
        var results = new List<BenchmarkOperationResult>(); long prepBytes = 0; double prepSeconds = 0, cleanupSeconds = 0; string? reason = null; BenchmarkCompletion completion = BenchmarkCompletion.Completed;
        try
        {
            if (!existing)
            {
                long start = Stopwatch.GetTimestamp(); progress?.Report(new("Preparing bounded benchmark file", null, 0, configuration.FileSizeBytes, 0));
                await using var stream = Open(owned.FilePath, FileMode.Create, FileAccess.ReadWrite, configuration.SequentialBlockBytes, FileOptions.SequentialScan);
                stream.SetLength(configuration.FileSizeBytes);
                if (configuration.HasReadWork)
                {
                    byte[] pattern = Pattern(Math.Min(configuration.SequentialBlockBytes, 4 * 1024 * 1024));
                    for (long offset = 0; offset < configuration.FileSizeBytes; offset += pattern.Length)
                    { token.ThrowIfCancellationRequested(); int count = (int)Math.Min(pattern.Length, configuration.FileSizeBytes - offset); await stream.WriteAsync(pattern.AsMemory(0, count), token); prepBytes += count; progress?.Report(new("Preparing bounded benchmark file", null, prepBytes, configuration.FileSizeBytes, Elapsed(start))); }
                    await stream.FlushAsync(token); stream.Flush(true);
                }
                prepSeconds = Elapsed(start);
            }
            foreach (var op in Ordered(configuration.Operations))
            {
                var result = await MeasureAsync(owned.FilePath, configuration, op, progress, token); results.Add(result);
                if (result.State == BenchmarkOperationState.Cancelled) { completion = BenchmarkCompletion.Cancelled; reason = "Cancelled by user; partial measurement is incomplete."; break; }
                if (result.State == BenchmarkOperationState.Failed) { completion = BenchmarkCompletion.Failed; reason = result.Error; break; }
            }
        }
        catch (OperationCanceledException) { completion = BenchmarkCompletion.Cancelled; reason = "Cancelled during preparation; no completed measurement published."; }
        catch (Exception ex) { completion = BenchmarkCompletion.Failed; reason = ex.Message; }
        finally
        {
            long start = Stopwatch.GetTimestamp();
            try { await files.CleanupAsync(owned); cleanup = true; }
            catch (Exception ex) { reason = string.IsNullOrWhiteSpace(reason) ? $"Cleanup failed; owned file remains at {owned.FilePath}: {ex.Message}" : $"{reason} Cleanup failed; owned file remains at {owned.FilePath}: {ex.Message}"; }
            cleanupSeconds = Elapsed(start);
        }
        return new(id, started, DateTimeOffset.UtcNow, BenchmarkPolicy.Version, typeof(FileBenchmarkEngine).Assembly.GetName().Version?.ToString() ?? "Unavailable",
            target, configuration, results, completion, reason, prepBytes, prepSeconds, 0, cleanupSeconds, Elapsed(totalStart), consentRequired, consentGranted,
            null, null, owned.FilePath, cleanup);
    }

    private static async Task<BenchmarkOperationResult> MeasureAsync(string path, BenchmarkConfiguration c, BenchmarkOperation op, IProgress<BenchmarkProgress>? progress, CancellationToken token)
    {
        int block = op is BenchmarkOperation.SequentialRead or BenchmarkOperation.SequentialWrite ? c.SequentialBlockBytes : c.RandomBlockBytes;
        long plannedOps = op is BenchmarkOperation.SequentialRead or BenchmarkOperation.SequentialWrite ? c.FileSizeBytes / block * c.Iterations : (long)c.RandomOperationCount * c.Iterations;
        long plannedBytes = plannedOps * block, bytes = 0, operations = 0, measurementStart = Stopwatch.GetTimestamp(), latencyTicks = 0, minimum = long.MaxValue, maximum = 0;
        byte[] buffer = Pattern(block); bool writing = op is BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite;
        var options = op is BenchmarkOperation.SequentialRead or BenchmarkOperation.SequentialWrite ? FileOptions.SequentialScan : FileOptions.RandomAccess;
        try
        {
            await using var stream = Open(path, FileMode.Open, writing ? FileAccess.ReadWrite : FileAccess.Read, block, options);
            var random = new Random(0x50495849);
            for (int iteration = 0; iteration < c.Iterations; iteration++)
            {
                long count = op is BenchmarkOperation.SequentialRead or BenchmarkOperation.SequentialWrite ? c.FileSizeBytes / block : c.RandomOperationCount;
                if (op is BenchmarkOperation.SequentialRead or BenchmarkOperation.SequentialWrite) stream.Position = 0;
                for (long i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (op is BenchmarkOperation.RandomRead or BenchmarkOperation.RandomWrite) stream.Position = random.NextInt64(c.FileSizeBytes / block) * block;
                    long one = Stopwatch.GetTimestamp();
                    if (writing) await stream.WriteAsync(buffer, token); else { int read = 0; while (read < block) { int n = await stream.ReadAsync(buffer.AsMemory(read, block - read), token); if (n == 0) throw new EndOfStreamException(); read += n; } }
                    long ticks = Stopwatch.GetTimestamp() - one; latencyTicks += ticks; minimum = Math.Min(minimum, ticks); maximum = Math.Max(maximum, ticks); bytes += block; operations++;
                    progress?.Report(new($"Measuring {op}", op, bytes, plannedBytes, Elapsed(measurementStart)));
                }
            }
            if (writing) await stream.FlushAsync(token);
            long elapsed = Stopwatch.GetTimestamp() - measurementStart;
            return BenchmarkOperationResult.Calculate(op, bytes, operations, elapsed, Latency(latencyTicks, minimum, maximum, operations));
        }
        catch (OperationCanceledException)
        { return BenchmarkOperationResult.Calculate(op, bytes, operations, Stopwatch.GetTimestamp() - measurementStart, Latency(latencyTicks, minimum, maximum, operations), BenchmarkOperationState.Cancelled, "Incomplete measurement; cancelled."); }
        catch (Exception ex)
        { return BenchmarkOperationResult.Calculate(op, bytes, operations, Stopwatch.GetTimestamp() - measurementStart, Latency(latencyTicks, minimum, maximum, operations), BenchmarkOperationState.Failed, ex.Message); }
    }
    private static BenchmarkLatency? Latency(long total, long min, long max, long samples) => samples == 0 ? null : new(total * 1000d / Stopwatch.Frequency / samples, min * 1000d / Stopwatch.Frequency, max * 1000d / Stopwatch.Frequency, samples);
    private static FileStream Open(string path, FileMode mode, FileAccess access, int buffer, FileOptions hint) => new(path, mode, access, FileShare.Read, Math.Clamp(buffer, 4096, 1024 * 1024), FileOptions.Asynchronous | hint);
    private static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
    private static IEnumerable<BenchmarkOperation> Ordered(BenchmarkOperation selected)
    { foreach (var op in new[] { BenchmarkOperation.SequentialWrite, BenchmarkOperation.SequentialRead, BenchmarkOperation.RandomWrite, BenchmarkOperation.RandomRead }) if ((selected & op) != 0) yield return op; }
    private static byte[] Pattern(int size)
    {
        var bytes = new byte[size]; ulong x = 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < size; i += 8) { x += 0x9E3779B97F4A7C15UL; ulong z = x; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; z ^= z >> 31; BitConverter.TryWriteBytes(bytes.AsSpan(i, Math.Min(8, size - i)), z); }
        return bytes;
    }
}
