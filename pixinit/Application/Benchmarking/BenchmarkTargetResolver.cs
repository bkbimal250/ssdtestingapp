using pixinit.Core.Benchmark;
using System.IO;
using pixinit.Core.Devices;
using pixinit.Core.History;

namespace pixinit.Application.Benchmarking;

public static class BenchmarkTargetResolver
{
    public static BenchmarkTarget Resolve(string directory, IReadOnlyList<StorageDevice> devices)
    {
        string full = Path.GetFullPath(directory); if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        string root = Path.GetPathRoot(full) ?? throw new InvalidOperationException("Target does not have a filesystem root.");
        var drive = new DriveInfo(root); if (!drive.IsReady) throw new InvalidOperationException("Target filesystem is not ready.");
        var matches = devices.Where(d => d.MountPoints.Any(m => string.Equals(Path.GetPathRoot(Path.GetFullPath(m)), root, StringComparison.OrdinalIgnoreCase))).ToArray();
        bool reliable = matches.Length == 1 && matches[0].InterfacePath is not null && matches[0].InstanceId is not null && matches[0].Serial is not null;
        return new(full, root, drive.DriveFormat, drive.AvailableFreeSpace, matches.Length == 1 ? SnapshotFactory.DeviceSnapshot(matches[0]) : null,
            matches.Length == 1 ? $"Filesystem root {root} maps to one discovered device in this session." : $"Filesystem root {root} maps to {matches.Length} discovered devices; physical identity comparison disabled.", reliable);
    }
}
