namespace pixinit.Core.Benchmark;

[Flags]
public enum BenchmarkProfiles { None = 0, Seq1MQ8T1 = 1, Seq1MQ1T1 = 2, Rnd4KQ32T16 = 4, Rnd4KQ1T1 = 8, All = 15 }
public sealed record BenchmarkProfile(BenchmarkProfiles Id, string Name, int BlockBytes, int QueuePerWorker, int Workers, bool Random)
{
    public static IReadOnlyList<BenchmarkProfile> All { get; } = [
        new(BenchmarkProfiles.Seq1MQ8T1, "SEQ1M Q8T1", 1048576, 8, 1, false),
        new(BenchmarkProfiles.Seq1MQ1T1, "SEQ1M Q1T1", 1048576, 1, 1, false),
        new(BenchmarkProfiles.Rnd4KQ32T16, "RND4K Q32T16", 4096, 32, 16, true),
        new(BenchmarkProfiles.Rnd4KQ1T1, "RND4K Q1T1", 4096, 1, 1, true)];
}
public sealed record BenchmarkPassResult(string Profile, int Pass, int QueuePerWorker, int Workers, BenchmarkOperationResult Measurement)
{
    public bool IsValidForDeviceAttribution { get; init; } = true;
    public bool IncludeInAverage { get; init; } = true;
    public double ElapsedSeconds => Measurement.MeasuredSeconds;
    public long TotalBytes => Measurement.BytesProcessed;
    public string Display => $"Pass {Pass} {Measurement.Operation}: {Measurement.MegabytesPerSecond:F2} MB/s ({ElapsedSeconds:F2}s, {TotalBytes / 1073741824d:F3} GiB); {Measurement.State}; {(IncludeInAverage ? "included" : "excluded")}";
}
public sealed record BenchmarkProfileSummary(string Profile, BenchmarkOperation Operation, int CompletedPasses, double AverageMBs, double BestMBs, double AverageIOPS, string PassValues);
public static class DirectBenchmarkPolicy
{
    public const long MaximumApplicationWrites = 96L * 1073741824;
    public const string Methodology = "Unbuffered filesystem I/O with write-through; OS file-data cache bypass requested; controller cache may remain. Queue depth is per worker. Preparation, allocation and pre-test flush excluded; completions/drain and post-write flush included. No raw sectors or volume locks.";
    public static string Describe(BenchmarkConfiguration c) => Methodology + (c.TimedMode ? $" Timed submission window {c.DurationSeconds}s; circular file reuse; byte ceiling can terminate a write early, excluding it from averages." : " Fixed byte/operation workload.") + " Two-second inter-pass pause is not guaranteed SLC recovery. Comparison methodology differs from CDM timed workload.";
    public static long TimedWriteAllowance(BenchmarkConfiguration c)
    {
        int jobs = BenchmarkProfile.All.Count(p => (c.Profiles & p.Id) != 0 && (c.Operations & (p.Random ? BenchmarkOperation.RandomWrite : BenchmarkOperation.SequentialWrite)) != 0) * c.Iterations;
        return jobs == 0 ? 0 : ((MaximumApplicationWrites - c.FileSizeBytes) / jobs / 1048576) * 1048576;
    }
    public static long WriteBudget(BenchmarkConfiguration c) => c.TimedMode ? checked(c.FileSizeBytes + TimedWriteAllowance(c) * BenchmarkProfile.All.Count(p => (c.Profiles & p.Id) != 0 && (c.Operations & (p.Random ? BenchmarkOperation.RandomWrite : BenchmarkOperation.SequentialWrite)) != 0) * c.Iterations + ((c.Operations & BenchmarkOperation.SustainedWrite) != 0 ? c.SustainedMaximumWriteBytes : 0)) : checked((c.HasReadWork ? c.FileSizeBytes : 0) +
        BenchmarkProfile.All.Where(p => (c.Profiles & p.Id) != 0).Sum(p =>
            (c.Operations & (p.Random ? BenchmarkOperation.RandomWrite : BenchmarkOperation.SequentialWrite)) != 0
            ? checked((p.Random ? (long)c.RandomOperationCount * p.BlockBytes : c.FileSizeBytes) * c.Iterations) : 0) +
        ((c.Operations & BenchmarkOperation.SustainedWrite) != 0 ? c.SustainedMaximumWriteBytes : 0));
    public static void Validate(BenchmarkConfiguration c, long available)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new InvalidOperationException("Unbuffered benchmarking requires Windows x64.");
        if (c.FileSizeBytes is < 1048576 or > 4294967296 || c.FileSizeBytes % 1048576 != 0) throw new InvalidOperationException("Unbuffered files require 1 MiB alignment, up to 4 GiB. UI sizes are 1, 2 and 4 GiB.");
        if (c.Iterations is not (1 or 3 or 5 or 9) || c.RandomOperationCount is < 1 or > 65536) throw new InvalidOperationException("Select 1, 3, 5 or 9 passes and at most 65536 random operations per pass/profile.");
        if (c.Profiles == BenchmarkProfiles.None || (c.Profiles & ~BenchmarkProfiles.All) != 0) throw new InvalidOperationException("Select at least one supported queue profile.");
        if (c.Operations == BenchmarkOperation.None || (c.Operations & ~(BenchmarkOperation.SequentialRead | BenchmarkOperation.SequentialWrite | BenchmarkOperation.RandomRead | BenchmarkOperation.RandomWrite | BenchmarkOperation.SustainedWrite)) != 0) throw new InvalidOperationException("Select supported benchmark operations.");
        if (!BenchmarkProfile.All.Any(p => (c.Profiles & p.Id) != 0 && (c.Operations & (p.Random ? BenchmarkOperation.RandomRead | BenchmarkOperation.RandomWrite : BenchmarkOperation.SequentialRead | BenchmarkOperation.SequentialWrite)) != 0) && (c.Operations & BenchmarkOperation.SustainedWrite) == 0) throw new InvalidOperationException("Selected profiles contain no enabled operations.");
        if ((c.Operations & BenchmarkOperation.SustainedWrite) != 0 && (c.SustainedDurationSeconds is < 1 or > 30 || c.SustainedMaximumWriteBytes is < 1048576 or > 34359738368 || c.SustainedMaximumWriteBytes % 1048576 != 0)) throw new InvalidOperationException("Sustained testing requires 1-30 seconds and at most 32 GiB aligned application writes.");
        if (c.TimedMode && c.DurationSeconds is not (5 or 10 or 20)) throw new InvalidOperationException("Timed duration must be 5, 10 or 20 seconds.");
        long ordinary = WriteBudget(c with { Operations = c.Operations & ~BenchmarkOperation.SustainedWrite });
        if (ordinary > MaximumApplicationWrites) throw new InvalidOperationException($"Requested {ordinary:N0} application bytes exceeds the unbuffered 96 GiB limit.");
        long required = checked(c.RequiredFileBytes + BenchmarkPolicy.SafetyMargin(available));
        if (available < required) throw new InvalidOperationException($"Insufficient free space: {required:N0} bytes required including reserve; {available:N0} available.");
    }
    public static IReadOnlyList<BenchmarkProfileSummary> Summarize(IEnumerable<BenchmarkPassResult> results) => results
        .Where(r => r.Measurement.State == BenchmarkOperationState.Completed && r.IncludeInAverage).GroupBy(r => (r.Profile, r.Measurement.Operation)).Select(g =>
            new BenchmarkProfileSummary(g.Key.Profile, g.Key.Operation, g.Count(), g.Average(r => r.Measurement.MegabytesPerSecond), g.Max(r => r.Measurement.MegabytesPerSecond), g.Average(r => r.Measurement.CompletedOperations / r.Measurement.MeasuredSeconds), string.Join(", ", g.Select(r => r.Measurement.MegabytesPerSecond.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))))).ToArray();
    public static string Plausibility(BenchmarkTarget target, IEnumerable<BenchmarkOperationResult> results)
    {
        if (!target.PhysicalIdentityReliable) return "Physical device attribution uncertain; do not compare to a drive specification.";
        if (target.Device?.Bus.Contains("Usb", StringComparison.OrdinalIgnoreCase) == true) return "USB bridge - performance may be limited by the bridge as well as the drive.";
        if (target.Device?.Protocol.Equals("Sata", StringComparison.OrdinalIgnoreCase) == true && results.Any(r => r.State == BenchmarkOperationState.Completed && r.Operation is BenchmarkOperation.SequentialRead or BenchmarkOperation.SequentialWrite && r.MegabytesPerSecond > 600)) return "INVALID for single SATA device attribution: measured sequential speed exceeds 600 MB/s. Check mapping, caching and measurement conditions. Original value retained; do not use as device speed.";
        return "No model-specific performance limit validated. " + (target.Device is { PcieGeneration: int generation, PcieLaneWidth: int width }
            ? $"Windows discovery observed PCIe {generation}.0 x{width}. " : "PCIe link not reported by Windows. ") + "Speed alone does not establish device health or an SLC defect.";
    }
}