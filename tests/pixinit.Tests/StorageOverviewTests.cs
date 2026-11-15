using System.IO;
using pixinit.Core.Devices;
using pixinit.ViewModels.Sata;
using pixinit.ViewModels.Nvme;

namespace pixinit.Tests;
internal static partial class Program
{
    private static void StorageSelectionUiTests()
    {
        var nvmeDisk = Drive("disk2", StorageProtocol.Nvme, true) with { MountPoints = [@"C:\", @"E:\"], CapacityBytes = 512_000_000_000 };
        var sataDisk = Drive("disk0", StorageProtocol.Sata) with { MountPoints = [@"D:\"], CapacityBytes = 256_000_000_000 };
        var emptyDisk = Drive("disk1", StorageProtocol.Sata) with { MountPoints = [], CapacityBytes = 128_020_000_000 };
        var discovery = new ControlledDiscovery();
        discovery.Completion.SetResult([nvmeDisk, sataDisk, emptyDisk]);
        var shell = new pixinit.ViewModels.Shell.ShellViewModel(new pixinit.Application.Discovery.DiscoveryCoordinator(discovery));
        shell.Sata.ReadVolumeCapacity = shell.Nvme.ReadVolumeCapacity = _ => (100_000, 97_200);
        shell.StartInitialScanAsync().GetAwaiter().GetResult();
        var view = new pixinit.Views.Shared.FirstPageView { DataContext = shell.Nvme };
        var host = new System.Windows.Window { Content = view, Width = 900, Height = 700 };
        host.Show(); Pump();
        var picker = Find<System.Windows.Controls.ComboBox>(view).Single();
        void VerifyVolume(string expected, pixinit.ViewModels.Shared.DeviceViewModel model)
        {
            view.DataContext = model;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!model.StorageRefreshTask.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(1); }
            Pump();
            Check(model.StorageRefreshTask.IsCompleted && Equals(picker.SelectionBoxItem, expected) &&
                Equals(picker.SelectedItem, expected) && !picker.IsDropDownOpen && model.UsedPercentDisplay == "2.8% used",
                $"Physical disk selects and displays {expected} and refreshes usage without opening the volume dropdown");
        }
        try
        {
            Check(shell.SelectedDisk?.Id == nvmeDisk.Id, "Startup selects the system physical disk");
            VerifyVolume(@"C:\", shell.Nvme);
            string[] nvmeFields = ["Available spare", "Percentage Used (consumed)", "Critical warning", "Host reads", "Host writes"];
            Check(nvmeFields.All(label => Find<System.Windows.Controls.TextBlock>(view).Any(t => t.Text == label)),
                "NVMe overview renders all five protocol health and host-counter fields");
            shell.Nvme.SelectedVolume = @"E:\";
            shell.SelectedDisk = sataDisk; VerifyVolume(@"D:\", shell.Sata);
            Check(!Find<System.Windows.Controls.TextBlock>(view).Any(t => t.Text is "Available spare" or "Percentage Used (consumed)" or "Critical warning"),
                "SATA overview hides NVMe-only health fields");
            var payload = Convert.FromHexString(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silicon-motion-256gb.hex")).Trim());
            var attributes = pixinit.Core.Diagnostics.Sata.SmartDataParser.Parse(payload, null);
            var response = new pixinit.Core.Diagnostics.Sata.AtaCommandResult(pixinit.Core.Diagnostics.Sata.AtaOperation.SmartData,
                pixinit.Core.Diagnostics.Sata.AtaOutcome.Success, "Imported fixture", payload, [], DateTimeOffset.UtcNow);
            shell.Sata.Apply(pixinit.Core.Diagnostics.Sata.SataResultMapper.Map(sataDisk, null, attributes, null, [response], [])); Pump();
            Check(Find<System.Windows.Controls.TextBlock>(view).Any(t => t.Text == "Host writes") &&
                Find<System.Windows.Controls.TextBlock>(view).Any(t => t.Text == "Host reads") &&
                Find<System.Windows.Controls.TextBlock>(view).Any(t => t.Text.Contains("218.19 GiB")),
                "Supported SATA payload renders host counters and accurate GiB values in the actual WPF overview");
            shell.SelectedDisk = emptyDisk; Pump();
            Check(picker.IsVisible && picker.ActualWidth is >= 118 and <= 122 && Equals(picker.SelectedItem, "No drive letter - Unallocated") &&
                shell.Sata.HasNoVolumes && shell.Sata.StorageMessage.Contains("No drive letter") && shell.Sata.TotalStorageBytes == emptyDisk.CapacityBytes &&
                shell.Sata.UsedStorageBytes is null && shell.Sata.FreeStorageBytes is null,
                "Disk without a letter shows No drive letter - Unallocated in a 120 DIP dropdown and keeps physical capacity");
            Check(Find<System.Windows.Controls.Border>(view).Any(b => b.Background is System.Windows.Media.SolidColorBrush brush &&
                brush.Color == System.Windows.Media.Color.FromRgb(255, 248, 225)), "No-drive-letter notice has a yellow background");
            shell.SelectedDisk = nvmeDisk; VerifyVolume(@"C:\", shell.Nvme);
            shell.SelectedDisk = sataDisk; VerifyVolume(@"D:\", shell.Sata);
        }
        finally { host.Close(); shell.Close(); }
    }

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
        Check(sata.TotalStorageBytes == disk.CapacityBytes && sata.UsedStorageBytes is null && sata.FreeStorageBytes is null && sata.UsedPercent is null && sata.HasNoVolumes && sata.StorageMessage.Contains("No drive letter"), "No mounted volume preserves physical capacity without inventing used/free values");
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
        Check(nvme.UsedPercent == 75 && nvme.EntireSsdInfo.Count == 16 && sata.EntireSsdInfo.Count == 13 && sata.EntireSsdInfo.Any(r => r.Label == "Host writes") && nvme.EntireSsdInfo.Any(r => r.Label == "Host writes" && r.NeedsDiagnostics), "Protocol overviews expose sixteen NVMe fields and thirteen SATA fields, including host counters");
    }
}
