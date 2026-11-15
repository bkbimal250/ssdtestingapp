using pixinit.Application.Discovery;
using pixinit.Application.Scanning;
using pixinit.Core.Devices;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.Infrastructure.Windows.Sata;
using pixinit.Infrastructure.Windows.Nvme;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;
internal static partial class Program
{
    private static async Task ReadOnlyHardwareDiagnostics()
    {
        var gate = new StorageOperationGate();
        var shell = new ShellViewModel(new DiscoveryCoordinator(new WindowsDiskDiscovery(), gate),
            new SataOperationCoordinator(new WindowsSataDiagnosticsProvider(), gate),
            new NvmeOperationCoordinator(new WindowsNvmeDiagnosticsProvider(), gate), operationGate: gate);
        await shell.ScanAsync();
        Console.WriteLine($"Read-only discovery: {shell.State}; {shell.AllDevices.Count} disks. No benchmark or write test.");
        foreach (var disk in shell.AllDevices.ToArray())
        {
            shell.SelectedDisk = disk;
            pixinit.ViewModels.Shared.DeviceViewModel page = disk.Protocol == StorageProtocol.Nvme ? shell.Nvme : shell.Sata;
            await page.StorageRefreshTask;
            Console.WriteLine($"Disk {disk.DiskNumber}: model={disk.Model}; serial={disk.Serial}; firmware={disk.Firmware}; bytes={disk.CapacityBytes}; bus={disk.Bus}; native={disk.NativeBusType}; protocol={disk.Protocol}; media={disk.Media}; access={disk.Access}; bridge={disk.BridgeChip}; usb={disk.UsbSpeed}; link={disk.PcieLinkDisplay}; mounts={string.Join(",", disk.MountPoints)}; volume={page.SelectedVolume}; usage={page.UsedPercentDisplay}; SATA-query={disk.CanQuerySata}; NVMe-query={disk.CanQueryNvme}");
            for (int pass = 1; pass <= 2; pass++)
            {
                if (disk.CanQuerySata)
                {
                    await shell.ReadSataAsync();
                    Console.WriteLine($"  Pass {pass}: {shell.SataOperationStatus}; temperature={shell.Sata.Temperature}; writes={shell.Sata.HostWrites}; refresh={shell.ReadSataCommand.CanExecute(null)}");
                    if (shell.Sata.Result is { } result)
                    {
                        Console.WriteLine($"    Fields: model={shell.Sata.Model}; serial={shell.Sata.Serial}; temp={shell.Sata.Temperature}; reads={shell.Sata.HostReads}; writes={shell.Sata.HostWrites}; endurance={shell.Sata.Endurance}; usb-notice={shell.Benchmark.UsbSpeedNotice}");
                        Console.WriteLine($"    Crystal: {shell.Sata.CrystalSummary.Replace('\n', '|')}");
                        Console.WriteLine($"    SMART:\n{shell.Sata.SmartTable}");
                        foreach (var row in shell.Sata.EntireSsdInfo) Console.WriteLine($"    {row.Label}: {row.Value}");
                        foreach (var command in result.Commands) Console.WriteLine($"    {command.Operation}: {command.Outcome}; {command.Explanation}");
                    }
                }
                else if (disk.CanQueryNvme)
                {
                    await shell.ReadNvmeAsync();
                    Console.WriteLine($"  Pass {pass}: {shell.NvmeOperationStatus}; temperature={shell.Nvme.Temperature}; refresh={shell.ReadNvmeCommand.CanExecute(null)}");
                    if (shell.Nvme.Result is { } result)
                    {
                        Console.WriteLine($"    Fields: model={shell.Nvme.Model}; serial={shell.Nvme.Serial}; temp={shell.Nvme.Temperature}; power-on={shell.Nvme.PowerOnHours}; cycles={shell.Nvme.PowerCycles}; reads={shell.Nvme.HostReads}; writes={shell.Nvme.HostWrites}; spare={shell.Nvme.AvailableSpare}; used={shell.Nvme.PercentageUsed}; warning={shell.Nvme.CriticalWarning}; endurance={shell.Benchmark.FriendlyEndurance}");
                        Console.WriteLine($"    Crystal: {shell.Nvme.CrystalSummary.Replace('\n', '|')}");
                        foreach (var response in result.Responses) Console.WriteLine($"    {response.Operation}: {response.Outcome}; {response.Explanation}");
                    }
                }
                else Console.WriteLine("  No supported diagnostic route for this device.");
            }
            foreach (var row in shell.Qc.Rows) Console.WriteLine($"QC {row.Category}: {row.Status}\n{row.Value}\nReason: {row.Reason}");
            if (shell.Qc.Rows.Count != 5 || shell.Qc.Rows.Any(r => string.IsNullOrWhiteSpace(r.Reason) || r.Value.Contains("N/A"))) throw new InvalidOperationException("QC evidence incomplete");
            Console.WriteLine($"  Benchmark temperature: {shell.Benchmark.FriendlyTemperature}");
        }
        shell.Close();
    }
}

