using System.IO;
using pixinit.Core.Benchmark;
using pixinit.Core.Diagnostics.Common;
using pixinit.Infrastructure.Benchmarking;
using pixinit.Infrastructure.Logging;
using pixinit.ViewModels.Benchmark;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;
internal static partial class Program
{
    private static async Task NoPersistenceTests()
    {
        var assembly = typeof(BenchmarkViewModel).Assembly;
        Check(assembly.GetReferencedAssemblies().All(a => !a.Name!.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) && !a.Name.Contains("SQLitePCL", StringComparison.OrdinalIgnoreCase)), "Application assembly has no SQLite dependency");
        Check(assembly.GetTypes().All(t => t.Name is not ("SqliteHistoryStore" or "HistoryViewModel" or "BenchmarkDbContext")), "Database store, context and history view model are absent");
        Check(typeof(BenchmarkViewModel).GetConstructors().SelectMany(c => c.GetParameters()).All(p => !p.Name!.Contains("store", StringComparison.OrdinalIgnoreCase)), "Benchmark constructor requires no persistence service");
        Check(typeof(ShellViewModel).GetProperty("History") is null && typeof(ShellViewModel).GetProperty("HistoryVisible") is null, "Shell has no retained history state");
        Check(typeof(BenchmarkViewModel).GetProperty("Sessions") is null && typeof(BenchmarkViewModel).GetProperty("SelectedHistoryRow") is null, "Benchmark exposes only the current run, not saved sessions");
        Check(DiscoveryLog.FilePath == Path.Combine(Path.GetTempPath(), "PIXINIT_detect.log"), "Operational logging uses only the requested TEMP log");
        string root = Path.Combine(Path.GetTempPath(), "pixinit-no-persistence-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var config = BenchmarkPolicy.Quick with { Unbuffered = true, FileSizeBytes = BenchmarkPolicy.MiB, Operations = BenchmarkOperation.SequentialRead, Iterations = 1 };
            var target = new BenchmarkTarget(root, Path.GetPathRoot(root)!, "NTFS", 20L * 1073741824, null, "Disposable one MiB test", false);
            var session = await new FileBenchmarkEngine().RunAsync(target, config, true);
            Check(session.Completion == BenchmarkCompletion.Completed && session.Configuration.Unbuffered, "Bounded unbuffered run completes without a database");
            Check(session.CleanupSucceeded && !File.Exists(session.BenchmarkFilePath), "Database-free run closes and cleans its positively owned temporary file");
            Check(session.PreparationBytesWritten <= BenchmarkPolicy.MiB && session.Results.All(r => r.Operation == BenchmarkOperation.SequentialRead), "No-persistence validation writes at most one MiB preparation and measures reads only");
            ThermalInfo? current = new(null, null, ThermalState.Green, 30, "Injected current observation", DateTimeOffset.UtcNow);
            var vm = new BenchmarkViewModel(new FileBenchmarkEngine(), () => [], () => null, () => { }, thermal: () => current);
            vm.TargetDirectory = root; await vm.InitializeAsync(); vm.RefreshDiagnosticContext();
            Check(vm.Thermal?.ObservedC == 30 && vm.TemperatureDisplay.Contains("30.0"), "Temperature card reads current diagnostic context with no stored history");
            current = current with { ObservedC = 31 }; vm.RefreshDiagnosticContext();
            Check(vm.Thermal?.ObservedC == 31 && !vm.Status.Contains("history", StringComparison.OrdinalIgnoreCase), "Repeated temperature updates stay live and cannot fail a database UNIQUE constraint");
            Check(!Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(), "Run and initialization leave no database or saved history files in disposable target");
        }
        finally { Directory.Delete(root, true); }
    }
}
