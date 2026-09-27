namespace pixinit.Core.Benchmark;

public static class BenchmarkPolicy
{
    public const string Version = "benchmark-policy-v1";
    public const long MiB = 1_048_576;
    public const long MaximumFileBytes = 512 * MiB;
    public const long MaximumWriteBytes = 1_073_741_824;
    public const long MinimumSafetyMarginBytes = 1_073_741_824;
    public const int MaximumIterations = 4, MaximumRandomOperations = 65_536;
    public static BenchmarkConfiguration Quick => new(BenchmarkPreset.Quick, BenchmarkOperation.SequentialRead | BenchmarkOperation.SequentialWrite | BenchmarkOperation.RandomRead | BenchmarkOperation.RandomWrite, 32 * MiB, (int)MiB, 4096, 1, 1024, 1);
    public static BenchmarkConfiguration Standard => new(BenchmarkPreset.Standard, BenchmarkOperation.SequentialRead | BenchmarkOperation.SequentialWrite | BenchmarkOperation.RandomRead | BenchmarkOperation.RandomWrite, 128 * MiB, (int)MiB, 4096, 2, 8192, 1);
    public static long SafetyMargin(long availableBytes) => Math.Max(MinimumSafetyMarginBytes, availableBytes / 10);
    public static void Validate(BenchmarkConfiguration c, long availableBytes, bool existingPreparedFile = false)
    {
        if (c.Operations == BenchmarkOperation.None) throw new InvalidOperationException("Select at least one benchmark operation.");
        if (c.FileSizeBytes is < 16 * MiB or > MaximumFileBytes) throw new InvalidOperationException("Test file size must be 16–512 MiB.");
        if (c.SequentialBlockBytes is < 4096 or > 8 * 1_048_576 || c.FileSizeBytes % c.SequentialBlockBytes != 0) throw new InvalidOperationException("Sequential block size must be aligned, 4 KiB–8 MiB, and divide the file size.");
        if (c.RandomBlockBytes is < 4096 or > 1_048_576 || c.FileSizeBytes % c.RandomBlockBytes != 0) throw new InvalidOperationException("Random block size must be aligned, 4 KiB–1 MiB, and divide the file size.");
        if (c.Iterations is < 1 or > MaximumIterations || c.RandomOperationCount is < 1 or > MaximumRandomOperations || c.QueueDepth != 1) throw new InvalidOperationException("Iterations, random operation count, or queue depth exceeds benchmark-policy-v1 limits.");
        long writes = c.MaximumWriteBytes(existingPreparedFile); if (writes > MaximumWriteBytes) throw new InvalidOperationException("Requested workload exceeds the 1 GiB maximum write allowance.");
        long required = checked((existingPreparedFile ? 0 : c.FileSizeBytes) + SafetyMargin(availableBytes));
        if (availableBytes < required) throw new InvalidOperationException($"Insufficient free space. Required {required} bytes including safety margin; available {availableBytes} bytes.");
    }
}
