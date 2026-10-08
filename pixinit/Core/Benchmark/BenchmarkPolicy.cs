namespace pixinit.Core.Benchmark;

public static class BenchmarkPolicy
{
    public const string Version = "benchmark-policy-v3";
    public const long MiB = 1_048_576;
    public const long MaximumFileBytes = 1024 * MiB;
    public const long MaxOrdinaryApplicationDataWritesBytes = 3_221_225_472;
    public const long MaximumWriteBytes = MaxOrdinaryApplicationDataWritesBytes;
    public const long MinimumSafetyMarginBytes = MaxOrdinaryApplicationDataWritesBytes;
    public const long MinimumSustainedSafetyMarginBytes = 1_073_741_824;
    public const int MaximumIterations = 4, MaximumRandomOperations = 65_536;
    public static BenchmarkConfiguration Quick => new(BenchmarkPreset.Quick, BenchmarkOperation.SequentialRead | BenchmarkOperation.SequentialWrite | BenchmarkOperation.RandomRead | BenchmarkOperation.RandomWrite, 32 * MiB, (int)MiB, 4096, 1, 1000, 1);
    public static BenchmarkConfiguration Standard => new(BenchmarkPreset.Standard, BenchmarkOperation.SequentialRead | BenchmarkOperation.SequentialWrite | BenchmarkOperation.RandomRead | BenchmarkOperation.RandomWrite, 128 * MiB, (int)MiB, 4096, 2, 8192, 1);
    public static long SafetyMargin(long availableBytes, bool sustainedOnly = false) => Math.Max(sustainedOnly ? MinimumSustainedSafetyMarginBytes : MinimumSafetyMarginBytes, availableBytes / 10);
    public static void Validate(BenchmarkConfiguration c, long availableBytes, bool existingPreparedFile = false)
    {
        if (c.Operations == BenchmarkOperation.None) throw new InvalidOperationException("Select at least one benchmark operation.");
        if (c.FileSizeBytes is < 16 * MiB or > MaximumFileBytes) throw new InvalidOperationException("Test file size must be 16-1024 MiB.");
        if (c.SequentialBlockBytes is < 4096 or > 8 * 1_048_576 || c.FileSizeBytes % c.SequentialBlockBytes != 0) throw new InvalidOperationException("Sequential block size must be aligned, 4 KiB-8 MiB, and divide the file size.");
        if (c.RandomBlockBytes is < 4096 or > 1_048_576 || c.FileSizeBytes % c.RandomBlockBytes != 0) throw new InvalidOperationException("Random block size must be aligned, 4 KiB-1 MiB, and divide the file size.");
        if (c.Iterations is < 1 or > MaximumIterations || c.RandomOperationCount is < 1 or > MaximumRandomOperations || c.QueueDepth != 1) throw new InvalidOperationException("Iterations, random operation count, or queue depth exceeds benchmark-policy-v3 limits.");
        bool sustained = (c.Operations & BenchmarkOperation.SustainedWrite) != 0;
        if ((c.Operations & ~(BenchmarkOperation.SequentialRead | BenchmarkOperation.SequentialWrite | BenchmarkOperation.RandomRead | BenchmarkOperation.RandomWrite | BenchmarkOperation.SustainedWrite)) != 0) throw new InvalidOperationException("Unknown benchmark operation.");
        if (sustained && (c.SustainedDurationSeconds is < 1 or > 30 || c.SustainedMaximumWriteBytes is < MiB or > 32 * 1_073_741_824L || c.SustainedMaximumWriteBytes % MiB != 0)) throw new InvalidOperationException("Sustained writes require 1-30 seconds and an aligned 1 MiB-32 GiB byte budget.");
        long writes = (c with { Operations = c.Operations & ~BenchmarkOperation.SustainedWrite }).MaximumWriteBytes(existingPreparedFile);
        if (writes > MaxOrdinaryApplicationDataWritesBytes) throw new InvalidOperationException($"Requested workload {writes} bytes exceeds the {MaxOrdinaryApplicationDataWritesBytes} byte (3 GiB) maximum write allowance.");
        long required = checked((existingPreparedFile && !sustained ? 0 : c.RequiredFileBytes) + SafetyMargin(availableBytes, sustained && (c.Operations & ~BenchmarkOperation.SustainedWrite) == BenchmarkOperation.None));
        if (availableBytes < required) throw new InvalidOperationException($"Insufficient free space. Required {required} bytes including safety margin; available {availableBytes} bytes.");
    }
}
