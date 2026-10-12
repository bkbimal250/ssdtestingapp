using System.Diagnostics;
using System.IO;

namespace pixinit.Infrastructure.Benchmarking;

internal sealed class BenchmarkProcessLease : IDisposable
{
    private readonly FileStream stream;
    private BenchmarkProcessLease(FileStream stream) => this.stream = stream;
    internal static bool IsCompetingProcess(string name) => name.Equals("DiskMark64", StringComparison.OrdinalIgnoreCase) || name.Equals("DiskMark32", StringComparison.OrdinalIgnoreCase) || name.Equals("DiskMark64A", StringComparison.OrdinalIgnoreCase);
    internal static void EnsureNoCompetitor()
    {
        bool found = false;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { found |= IsCompetingProcess(process.ProcessName); }
                catch (InvalidOperationException) { }
            }
        }
        if (found) throw new InvalidOperationException("Another benchmark running - close CrystalDiskMark first. Concurrent tests can distort results.");
    }
    internal static BenchmarkProcessLease Acquire(string? path = null)
    {
        EnsureNoCompetitor();
        try
        {
            // CreateNew never deletes or modifies a pre-existing file, including a stale or replaced lock.
            return new(new FileStream(path ?? Path.Combine(Path.GetTempPath(), "PIXINIT_BENCHMARK_LOCK"), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
        }
        catch (IOException ex) { throw new InvalidOperationException("Another PIXINIT benchmark holds the lock, or a lock file already exists. Start was blocked.", ex); }
        catch (UnauthorizedAccessException ex) { throw new InvalidOperationException("Benchmark lock could not be created. Start was blocked.", ex); }
    }
    public void Dispose() => stream.Dispose();
}
