using System.Globalization;
using pixinit.Core.History;

namespace pixinit.Core.Benchmark;

[Flags]
public enum BenchmarkOperation { None = 0, SequentialRead = 1, SequentialWrite = 2, RandomRead = 4, RandomWrite = 8 }
public enum BenchmarkPreset { Quick, Standard, Custom }
public enum BenchmarkCompletion { Completed, Cancelled, Failed }
public enum BenchmarkOperationState { Pending, Running, Completed, Cancelled, Failed }

public sealed record BenchmarkConfiguration(BenchmarkPreset Preset, BenchmarkOperation Operations, long FileSizeBytes,
    int SequentialBlockBytes, int RandomBlockBytes, int Iterations, int RandomOperationCount, int QueueDepth,
    string IoMode = "Buffered asynchronous filesystem I/O; QD1; measured writes include FlushAsync to the OS; no write-through or durable Flush(true)")
{
    public bool HasWriteWork => (Operations & (BenchmarkOperation.SequentialWrite | BenchmarkOperation.RandomWrite)) != 0;
    public bool HasReadWork => (Operations & (BenchmarkOperation.SequentialRead | BenchmarkOperation.RandomRead)) != 0;
    public string Signature => $"{Preset}|{Operations}|{FileSizeBytes}|{SequentialBlockBytes}|{RandomBlockBytes}|{Iterations}|{RandomOperationCount}|{QueueDepth}|{IoMode}";
    public long MaximumWriteBytes(bool existingPreparedFile = false) => checked((HasReadWork && !existingPreparedFile ? FileSizeBytes : 0) +
        ((Operations & BenchmarkOperation.SequentialWrite) != 0 ? FileSizeBytes * Iterations : 0) +
        ((Operations & BenchmarkOperation.RandomWrite) != 0 ? (long)RandomBlockBytes * RandomOperationCount * Iterations : 0));
}

public sealed record BenchmarkTarget(string Directory, string VolumeRoot, string FileSystem, long AvailableBytes,
    SnapshotDevice? Device, string MappingEvidence, bool PhysicalIdentityReliable);
public sealed record BenchmarkLatency(double AverageMilliseconds, double MinimumMilliseconds, double MaximumMilliseconds, long Samples);
public sealed record BenchmarkOperationResult(BenchmarkOperation Operation, BenchmarkOperationState State, long BytesProcessed,
    long CompletedOperations, double MeasuredSeconds, double BytesPerSecond, double MegabytesPerSecond, double MebibytesPerSecond,
    double? Iops, BenchmarkLatency? Latency, string? Error)
{
    public static BenchmarkOperationResult Calculate(BenchmarkOperation operation, long bytes, long operations, long elapsedTicks, BenchmarkLatency? latency, BenchmarkOperationState state = BenchmarkOperationState.Completed, string? error = null)
    {
        double seconds = elapsedTicks / (double)System.Diagnostics.Stopwatch.Frequency;
        double bps = seconds > 0 ? bytes / seconds : 0;
        return new(operation, state, bytes, operations, seconds, bps, bps / 1_000_000d, bps / 1_048_576d,
            operation is BenchmarkOperation.RandomRead or BenchmarkOperation.RandomWrite && seconds > 0 ? operations / seconds : null, latency, error);
    }
}
public sealed record BenchmarkTemperature(double? Celsius, string Availability, string Source, string Scope, DateTimeOffset? ObservedAt);
public sealed record BenchmarkSession(Guid Id, DateTimeOffset StartedUtc, DateTimeOffset FinishedUtc, string PolicyVersion,
    string ApplicationVersion, BenchmarkTarget Target, BenchmarkConfiguration Configuration, IReadOnlyList<BenchmarkOperationResult> Results,
    BenchmarkCompletion Completion, string? Reason, long PreparationBytesWritten, double PreparationSeconds, double WarmupSeconds,
    double CleanupSeconds, double TotalSeconds, bool WriteConsentRequired, bool WriteConsentGranted,
    BenchmarkTemperature? BeforeTemperature, BenchmarkTemperature? AfterTemperature,
    string? BenchmarkFilePath, bool CleanupSucceeded,
    string Methodology = "Buffered filesystem benchmark — QD1; caching affects results. Measured writes include FlushAsync to the OS, not write-through or durable-media Flush(true). File creation, preparation, warm-up and cleanup are excluded from primary throughput. Sustained-write testing is not implemented.")
{
    public const string ReportSchemaVersion = "pixinit.benchmark-report/1.0";
    public string Summary => $"{Completion} · {string.Join(" · ", Results.Select(r => $"{r.Operation}: {r.MegabytesPerSecond:F1} MB/s"))}";
}
public sealed record BenchmarkProgress(string Stage, BenchmarkOperation? Operation, long BytesProcessed, long PlannedBytes, double ElapsedSeconds)
{ public double Percent => PlannedBytes <= 0 ? 0 : Math.Clamp(BytesProcessed * 100d / PlannedBytes, 0, 100); }

public sealed record BenchmarkComparison(BenchmarkOperation Operation, double Previous, double Current, double Delta, double PercentDelta);
public static class BenchmarkComparisons
{
    public static IReadOnlyList<BenchmarkComparison> Compare(BenchmarkSession previous, BenchmarkSession current)
    {
        if (previous.Completion != BenchmarkCompletion.Completed || current.Completion != BenchmarkCompletion.Completed ||
            !previous.Target.PhysicalIdentityReliable || !current.Target.PhysicalIdentityReliable ||
            previous.PolicyVersion != current.PolicyVersion || previous.Target.Device?.MatchKey != current.Target.Device?.MatchKey ||
            !string.Equals(previous.Target.VolumeRoot, current.Target.VolumeRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(previous.Target.FileSystem, current.Target.FileSystem, StringComparison.OrdinalIgnoreCase) ||
            previous.Configuration.Operations != current.Configuration.Operations ||
            previous.Configuration.FileSizeBytes != current.Configuration.FileSizeBytes || previous.Configuration.SequentialBlockBytes != current.Configuration.SequentialBlockBytes ||
            previous.Configuration.RandomBlockBytes != current.Configuration.RandomBlockBytes || previous.Configuration.Iterations != current.Configuration.Iterations ||
            previous.Configuration.RandomOperationCount != current.Configuration.RandomOperationCount || previous.Configuration.QueueDepth != current.Configuration.QueueDepth ||
            previous.Configuration.IoMode != current.Configuration.IoMode || previous.StartedUtc >= current.StartedUtc) return [];
        var old = previous.Results.Where(r => r.State == BenchmarkOperationState.Completed).ToDictionary(r => r.Operation);
        return current.Results.Where(r => r.State == BenchmarkOperationState.Completed && old.ContainsKey(r.Operation)).Select(r =>
        {
            double before = old[r.Operation].MegabytesPerSecond, delta = r.MegabytesPerSecond - before;
            return new BenchmarkComparison(r.Operation, before, r.MegabytesPerSecond, delta, before == 0 ? double.NaN : delta * 100d / before);
        }).ToArray();
    }
}
