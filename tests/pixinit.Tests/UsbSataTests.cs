using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Windows.Sata;
using pixinit.Application.Discovery;
using pixinit.Application.Scanning;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;
internal static partial class Program
{
    private static async Task UsbSataTests()
    {
        var usb = SyntheticSata() with { Bus = ConnectionBus.Usb, NativeBusType = 7, Model = "JMicron Tach", Serial = "BRIDGE-SERIAL", ProtocolHeuristic = true };
        Check(usb.CanQuerySata && !(usb with { Protocol = StorageProtocol.Nvme }).CanQuerySata, "SATA-classified USB can refresh; USB NVMe cannot enter ATA transport");
        SataIdentityGuard.Validate(usb, usb with { Protocol = StorageProtocol.Unknown, ProtocolHeuristic = false });
        bool refused = false;
        try { SataIdentityGuard.Validate(usb, usb with { Serial = "REPLACEMENT" }); }
        catch (AtaTransportException ex) { refused = ex.Outcome == AtaOutcome.IdentityMismatch; }
        Check(refused, "USB metadata identity is checked even for heuristic classification");
        foreach (bool twelve in new[] { false, true })
        {
            foreach (var operation in Enum.GetValues<AtaOperation>())
            {
                var cdb = SatCommandPolicy.Request(operation, twelve);
                Check(cdb.Length == (twelve ? 12 : 16) && cdb[0] == (twelve ? 0xA1 : 0x85) &&
                    cdb[twelve ? 9 : 14] == (operation == AtaOperation.Identify ? 0xEC : 0xB0), "SAT allowlist builds read-only command in each CDB format");
                if (operation != AtaOperation.Identify)
                    Check(cdb[twelve ? 6 : 10] == 0x4F && cdb[twelve ? 7 : 12] == 0xC2, "SAT SMART signature occupies low LBA register bytes");
            }
        }
        var sense = new byte[22]; sense[0] = 0x72; sense[1] = 1; sense[3] = 0x1D; sense[7] = 14;
        sense[8] = 9; sense[9] = 12; sense[17] = 0x4F; sense[19] = 0xC2; sense[21] = 0x50;
        var status = SatCommandPolicy.Validate(AtaOperation.SmartStatus, 2, [], 0, sense);
        Check(status.Succeeded && SmartReturnStatus.Interpret(status) == true, "SAT recovered-error sense with ATA registers decodes SMART status");
        Check(!SatCommandPolicy.Validate(AtaOperation.SmartStatus, 0, [], 0, []).Succeeded, "SCSI success without return registers cannot fabricate SMART PASS");
        Check(!SatCommandPolicy.Validate(AtaOperation.SmartData, 0, new byte[512], 100, []).Succeeded, "Short USB SMART transfers are rejected");
        sense[1] = 5;
        Check(SatCommandPolicy.Validate(AtaOperation.Identify, 2, new byte[512], 512, sense).Outcome == AtaOutcome.Unsupported, "Illegal SAT command permits bounded alternate-CDB fallback");
        var transport = new SyntheticTransport();
        var provider = new WindowsSataDiagnosticsProvider(transport);
        var result = await provider.ReadAsync(usb, default);
        Check(result.Attributes.Count > 0 && transport.Operations.Count == 4 && result.Discrepancies.Contains("USB enclosure serial"),
            "USB bridge serial differs legitimately from ATA serial without preventing SMART reads");
        var discovery = new ControlledDiscovery(); discovery.Completion.SetResult([usb]);
        var gate = new StorageOperationGate();
        var shell = new ShellViewModel(new DiscoveryCoordinator(discovery, gate), new SataOperationCoordinator(provider, gate));
        await shell.ScanAsync();
        Check(shell.ReadSataCommand.CanExecute(null), "USB SATA refresh command is enabled when idle");
        await shell.ReadSataAsync(); await shell.ReadSataAsync();
        Check(shell.Sata.Result is not null && shell.ReadSataCommand.CanExecute(null) && transport.Opens == 3, "Repeated USB refresh performs new reads and re-enables the button");
        transport.CommandError = AtaOutcome.Unsupported;
        await shell.ReadSataAsync();
        Check(shell.ReadSataCommand.CanExecute(null) && shell.SataOperationStatus.Contains("Refresh to retry"), "Unsupported USB bridge has an explanation and permits retry");
        shell.Close();
    }
}
