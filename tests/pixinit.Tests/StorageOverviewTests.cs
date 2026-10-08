using System.IO;
using pixinit.Core.Devices;
using pixinit.ViewModels.Sata;
using pixinit.ViewModels.Nvme;

namespace pixinit.Tests;
internal static partial class Program
{
    private static async Task StorageOverviewTests()
    {
        const long gib = 1073741824;
        var sata = new SataViewModel { ReadVolumeCapacity = volume => volume == @"C:\" ? (100 * gib, 25 * gib) : (50 * gib, 40 * gib) };
        var disk = Drive("usage", StorageProtocol.Sata) with { CapacityBytes = 200 * gib, MountPoints = [@"C:\", @"D:\", @"c:\", @"C:\mounted-volume\", "Unmounted volume"] };
        sata.SetDevice(disk); await sata.RefreshStorageAsync();
        Check(sata.Volumes.SequenceEqual(new[] { @"C:\", @"D:\" }) && sata.SelectedVolume == @"C:\", "Mount letters retain drive roots, deduplicate and exclude mounted folders rather than reading their host disk");
        Check(sata.TotalStorageBytes == 100 * gib && sata.FreeStorageBytes == 25 * gib && sata.UsedStorageBytes == 75 * gib && sata.UsedPercent == 75, "Selected volume total, available free, used bytes and percentage are exact");
        Check(sata.TotalStorageDisplay.Contains("100.00 GiB") && sata.TotalStorageDisplay.Contains("107.37 GB"), "Volume display separates binary GiB from decimal GB");
        sata.SelectedVolume = @"D:\"; await sata.RefreshStorageAsync();
        Check(sata.TotalStorageBytes == 50 * gib && sata.UsedPercent == 20, "Changing mounted volume updates usage without aggregating disk capacities");
        sata.SelectedVolume = @"Z:\";
        Check(sata.SelectedVolume == @"D:\", "Unmapped volume selection is refused");
        sata.SetDevice(disk with { MountPoints = [] }); await sata.RefreshStorageAsync();
        Check(sata.TotalStorageBytes == disk.CapacityBytes && sata.UsedStorageBytes is null && sata.FreeStorageBytes is null && sata.UsedPercent is null && sata.StorageMessage == "No mounted volume - showing device capacity only", "No mounted volume preserves physical capacity without inventing used/free values");
        sata.ReadVolumeCapacity = _ => throw new IOException("Test volume not ready");
        sata.SetDevice(disk); await sata.RefreshStorageAsync();
        Check(!sata.HasStorageUsage && sata.StorageMessage.Contains("Volume usage unavailable"), "Unavailable filesystem volume clears stale usage and preserves device capacity");
        var delayed = new TaskCompletionSource<(long, long)>(TaskCreationOptions.RunContinuationsAsynchronously);
        sata.ReadVolumeCapacity = _ => delayed.Task.GetAwaiter().GetResult();
        sata.SetDevice(disk); var pending = sata.RefreshStorageAsync();
        sata.SetDevice(null); delayed.SetResult((100 * gib, 25 * gib)); await pending;
        Check(sata.TotalStorageBytes is null && sata.FreeStorageBytes is null && sata.SelectedVolume == "", "Late capacity result cannot repopulate a different or cleared selection");
        var nvme = new NvmeViewModel { ReadVolumeCapacity = _ => (80 * gib, 20 * gib) };
        nvme.SetDevice(disk with { Protocol = StorageProtocol.Nvme }); await nvme.RefreshStorageAsync();
        Check(nvme.UsedPercent == 75 && nvme.EntireSsdInfo.Count == 16 && sata.EntireSsdInfo.Count == 16 && nvme.EntireSsdInfo.Any(r => r.Label == "Host writes" && r.NeedsDiagnostics), "Both protocol overviews expose sixteen availability-aware fields and missing-data diagnostic action");
    }
}
