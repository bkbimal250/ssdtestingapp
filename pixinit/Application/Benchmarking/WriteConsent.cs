using System.Windows;
using pixinit.Core.Benchmark;

namespace pixinit.Application.Benchmarking;

public interface IWriteConsent
{ Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes); }

public sealed class WpfWriteConsent : IWriteConsent
{
    public Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes)
    {
        string message = $"PIXINIT will write at most {maximumWriteBytes:N0} application data bytes to a bounded temporary file on the selected filesystem. This is not a physical NAND-write estimate.\n\n" +
            "SSD/NVMe flash has finite write endurance. This workload can temporarily increase storage activity, temperature, and power use. Results vary with caches, filesystem, encryption, background applications, power mode, thermal state, free space, firmware, test size, and workload.\n\n" +
            "This is a buffered filesystem benchmark at queue depth 1; caching affects results. It does not measure durable-write latency or sustained post-cache speed. PIXINIT does not write raw sectors. Approve this exact target and workload?";
        return Task.FromResult(MessageBox.Show(message, "Approve temporary benchmark writes", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
    }
}
