using System.Windows;
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
        => Review(maximumWriteBytes, "Selected filesystem");
    public Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes, BenchmarkTarget target)
        => Review(maximumWriteBytes, target.Directory);
    private static Task<bool> Review(long maximumWriteBytes, string target)
    {
        string message = $"Run SSD speed test?\n\nLocation: {target}\nTemporary writes: up to {maximumWriteBytes / 1073741824d:F2} GiB. The test file is removed afterward.";
        return Task.FromResult(MessageBox.Show(message, "Confirm SSD Speed Test", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);
    }
}
