using System.Windows;
using System.IO;
using pixinit.Infrastructure.Benchmarking;
using pixinit.Core.Benchmark;

namespace pixinit.Application.Benchmarking;

public interface IWriteConsent
{
    Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes);
    Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes, BenchmarkTarget target) => RequestAsync(configuration, maximumWriteBytes);
}

public sealed class WpfWriteConsent : IWriteConsent
{
    public Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes)
        => Review(configuration, maximumWriteBytes, "Selected filesystem");
    public Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes, BenchmarkTarget target)
        => Review(configuration, maximumWriteBytes, $"{target.Directory} (volume {target.VolumeRoot}; {target.FileSystem})\n{target.MappingEvidence}\n" + (configuration.FilePlan is { } plan ? $"Will create {Path.Combine(target.Directory, OwnedBenchmarkFileManager.DirectoryName, plan.FileName)} ({configuration.RequiredFileBytes / 1073741824d:F3} GiB) and delete after the session." : "Randomized owned temporary filename."));
    private static Task<bool> Review(BenchmarkConfiguration configuration, long maximumWriteBytes, string target)
    {
        string message = $"Target: {target}\n\nPIXINIT will write at most {maximumWriteBytes:N0} application data bytes to a bounded temporary file on the selected filesystem. Temporary file size: {configuration.RequiredFileBytes:N0} bytes; randomized owned filename; deleted after completion/cancellation. This is not a physical NAND-write estimate.\n\n" +
            $"Sustained workload: {((configuration.Operations & BenchmarkOperation.SustainedWrite) != 0 ? $"up to {configuration.SustainedDurationSeconds} seconds, 1 GiB circular file, {configuration.SustainedMaximumWriteBytes:N0} application data bytes" : "Not selected")}.\n\n" +
            "SSD/NVMe flash has finite write endurance. This workload can temporarily increase storage activity, temperature, and power use. Results vary with caches, filesystem, encryption, background applications, power mode, thermal state, free space, firmware, test size, and workload.\n\n" +
            configuration.EffectiveIoMode + $"\n\nPasses: {configuration.Iterations}; profiles: {configuration.Profiles}. PIXINIT does not write raw sectors. Approve this exact target and workload?";
        return Task.FromResult(MessageBox.Show(message, "Approve temporary benchmark writes", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
    }
}
