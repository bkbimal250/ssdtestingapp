using System.IO;
using pixinit.Application.Benchmarking;
using pixinit.Application.Reporting;
using pixinit.Core.Benchmark;
using pixinit.Core.Devices;
using pixinit.Core.History;
using pixinit.Infrastructure.Benchmarking;
using pixinit.ViewModels.Benchmark;

namespace pixinit.Tests;

internal static partial class Program
{
    private static async Task Phase7Tests()
    {
        var config = BenchmarkPolicy.Quick;
        Check(config.IoMode.Contains("Buffered") && config.IoMode.Contains("QD1") && config.IoMode.Contains("FlushAsync") && config.IoMode.Contains("no write-through"), "Benchmark configuration persists buffering, queue and flush semantics");
        Check(new BenchmarkSession(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, BenchmarkPolicy.Version, "1", null!, config, [], BenchmarkCompletion.Completed, null, 0, 0, 0, 0, 0, true, true, null, null, null, true).Methodology.Contains("throughput drop does not prove SLC exhaustion"), "Methodology distinguishes bounded sustained writes from proven SLC exhaustion");

        string root = Path.Combine(Path.GetTempPath(), "pixinit-phase7-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var manager = new OwnedBenchmarkFileManager();
            var corrupt = await manager.CreateAsync(root, Guid.NewGuid(), default); await File.WriteAllTextAsync(corrupt.ManifestPath, "{broken");
            bool refused = false; try { await manager.CleanupAsync(corrupt); } catch (InvalidOperationException) { refused = true; }
            Check(refused && File.Exists(corrupt.FilePath), "Corrupted ownership manifest cannot authorize deletion");
            File.Delete(corrupt.FilePath); File.Delete(corrupt.ManifestPath);

            var replaced = await manager.CreateAsync(root, Guid.NewGuid(), default); File.Delete(replaced.FilePath); await File.WriteAllTextAsync(replaced.FilePath, "replacement");
            refused = false; try { await manager.CleanupAsync(replaced); } catch (InvalidOperationException) { refused = true; }
            Check(refused && await File.ReadAllTextAsync(replaced.FilePath) == "replacement", "Replaced benchmark file identity cannot authorize deletion");
            File.Delete(replaced.FilePath); File.Delete(replaced.ManifestPath);

            string volume = Path.GetPathRoot(root)!;
            StorageDevice Device(string id) => new(id, id, id, StorageProtocol.Nvme, ConnectionBus.Nvme, DiagnosticCapabilities.Identity, DeviceAccess.Available)
            { InterfacePath = "path-" + id, InstanceId = "instance-" + id, MountPoints = [volume], CapacityBytes = 1_000_000 };
            int calls = 0; var consent = new Consent(true);
            var vm = new BenchmarkViewModel(new FileBenchmarkEngine(manager), null, () => [Device(++calls == 1 ? "first" : "changed")], () => null, () => { }, consent)
            { TargetDirectory = root, Preset = BenchmarkPreset.Custom, FileSizeMiB = 16, SequentialWrite = false, RandomWrite = false, RandomRead = false };
            await vm.RunAsync();
            Check(consent.Calls == 1 && vm.Result is null && vm.Status.Contains("mapping changed") && !Directory.EnumerateFiles(Path.Combine(root, OwnedBenchmarkFileManager.DirectoryName), "*.bin").Any(), "Target mapping is revalidated after consent and before file creation");

            var snap = SnapshotFactory.DeviceSnapshot(Device("stable")); var target = new BenchmarkTarget(root, volume, new DriveInfo(root).DriveFormat, new DriveInfo(root).AvailableFreeSpace, snap, "test", true);
            var result = BenchmarkOperationResult.Calculate(BenchmarkOperation.SequentialRead, 1_000_000, 1, System.Diagnostics.Stopwatch.Frequency, null);
            var older = new BenchmarkSession(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1), BenchmarkPolicy.Version, "1", target, config, [result], BenchmarkCompletion.Completed, null, 0, 0, 0, 0, 1, true, true, null, null, null, true);
            var newer = older with { Id = Guid.NewGuid(), StartedUtc = DateTimeOffset.UtcNow, FinishedUtc = DateTimeOffset.UtcNow, Results = [result] };
            Check(BenchmarkComparisons.Compare(older, newer).Count == 1 && BenchmarkComparisons.Compare(older, newer with { PolicyVersion = "changed" }).Count == 0, "Comparison requires the same benchmark policy version");
            Check(BenchmarkComparisons.Compare(older, newer with { Target = target with { FileSystem = "changed" } }).Count == 0 && BenchmarkComparisons.Compare(older, newer with { Configuration = config with { Operations = BenchmarkOperation.SequentialRead } }).Count == 0, "Comparison requires the same filesystem and selected workload");

            string txt = Path.Combine(root, "report.txt"), json = Path.Combine(root, "report.json"); await BenchmarkReportExporter.ExportTextAsync(newer, txt); await BenchmarkReportExporter.ExportJsonAsync(newer, json);
            string report = await File.ReadAllTextAsync(txt), structured = await File.ReadAllTextAsync(json);
            Check(report.Contains(BenchmarkViewModel.BenchmarkLabel) && report.Contains("not physical NAND writes") && report.Contains("throughput drop does not prove SLC-cache exhaustion"), "TXT labels cached QD1 results and excludes NAND and sustained-speed claims");
            Check(structured.Contains("FlushAsync") && structured.Contains("VolumeRoot") && structured.Contains("Methodology"), "JSON persists flush behavior, target volume and timing methodology");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
