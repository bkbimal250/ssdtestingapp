using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using pixinit.Application.Discovery;
using pixinit.Application.Scanning;
using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Windows.Sata;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;

internal static partial class Program
{
    // All ATA byte buffers in this file are SYNTHETIC. None were captured from hardware.
    private static void Word(byte[] bytes, int word, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(word * 2), value);
    private static void AtaString(byte[] data, int start, int words, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text.PadRight(words * 2));
        for (int i = 0; i < words * 2; i += 2) { data[start * 2 + i] = bytes[i + 1]; data[start * 2 + i + 1] = bytes[i]; }
    }
    private static void Checksum(byte[] data) { data[511] = 0; data[511] = unchecked((byte)-data.Sum(b => (int)b)); }
    private static byte[] SyntheticIdentify(bool enabled = true)
    {
        var bytes = new byte[512]; Word(bytes, 0, 0x40); Word(bytes, 49, 0x200);
        AtaString(bytes, 10, 10, "SYNTHETIC-SERIAL"); AtaString(bytes, 23, 4, "T001"); AtaString(bytes, 27, 20, "SYNTHETIC SATA MODEL");
        Word(bytes, 82, 1); Word(bytes, 83, 0x4400); Word(bytes, 85, enabled ? (ushort)1 : (ushort)0); Word(bytes, 87, 0x4000);
        Word(bytes, 106, 0x6003); Word(bytes, 100, 0x4240); Word(bytes, 101, 15); // 1,000,000 sectors
        Word(bytes, 76, 0x10E); Word(bytes, 75, 31); Word(bytes, 80, 0x03E0); Word(bytes, 169, 1);
        Word(bytes, 217, 7200); Word(bytes, 168, 2); Word(bytes, 128, 9);
        bytes[510] = 0xA5; Checksum(bytes); return bytes;
    }
    private static byte[] SyntheticSmart(bool threshold = false)
    {
        var bytes = new byte[512]; Word(bytes, 0, 1);
        bytes[2] = threshold ? (byte)0xF2 : (byte)0xF1;
        bytes[14] = threshold ? (byte)0xF1 : (byte)0xF2;
        if (threshold) { bytes[3] = 10; bytes[15] = 20; }
        else
        {
            bytes[3] = 1; bytes[5] = 100; bytes[6] = 90; bytes[7] = 0x34; bytes[8] = 0x12;
            bytes[17] = 80; bytes[18] = 70; bytes[19] = 3;
        }
        Checksum(bytes); return bytes;
    }
    private static StorageDevice SyntheticSata(string id = "sata-a") =>
        Drive(id, StorageProtocol.Sata) with { Model = "SYNTHETIC SATA MODEL", Serial = "SYNTHETIC-SERIAL", Firmware = "T001",
            Bus = ConnectionBus.Sata, NativeBusType = 11, InterfacePath = "synthetic-interface-" + id, DiskNumber = 2, CapacityBytes = 512_000_000 };
    private static AtaCommandResult SyntheticResponse(AtaOperation op, byte[]? payload = null)
    {
        var registers = new byte[8]; registers[6] = 0x50;
        if (op == AtaOperation.SmartStatus) { registers[3] = 0x4F; registers[4] = 0xC2; }
        return new(op, AtaOutcome.Success, "Synthetic response", payload ?? op switch
        { AtaOperation.Identify => SyntheticIdentify(), AtaOperation.SmartData => SyntheticSmart(), AtaOperation.SmartThresholds => SyntheticSmart(true), _ => [] }, registers, DateTimeOffset.UtcNow);
    }
    private static void SataParserAndPolicyTests()
    {
        var id = AtaIdentifyParser.Parse(SyntheticIdentify());
        Check(id.Model == "SYNTHETIC SATA MODEL" && id.Serial == "SYNTHETIC-SERIAL" && id.Firmware == "T001", "Synthetic IDENTIFY word-swapped strings decode correctly");
        Check(id.LbaSupported == true && id.Lba48Supported == true && id.CapacityBytes == 512_000_000 && id.LogicalSectorBytes == 512 && id.PhysicalSectorBytes == 4096, "IDENTIFY LBA48 and validated logical/physical sector relationship");
        var data = SyntheticIdentify(); Word(data, 83, 0x4000); Word(data, 60, 1000); Checksum(data);
        Check(AtaIdentifyParser.Parse(data).CapacityBytes == 512_000, "IDENTIFY LBA28 capacity uses valid sector size");
        data = SyntheticIdentify(); Word(data, 106, 0x5000); Word(data, 117, 2048); Checksum(data);
        Check(AtaIdentifyParser.Parse(data).CapacityBytes == 4_096_000_000 && AtaIdentifyParser.Parse(data).LogicalSectorBytes == 4096, "4Kn identity does not assume 512-byte logical sectors");
        data = SyntheticIdentify(); Word(data, 106, 0); Checksum(data);
        Check(AtaIdentifyParser.Parse(data).CapacityBytes is null, "Invalid sector-size validity withholds byte capacity");
        data = SyntheticIdentify(); Word(data, 106, 0x5000); Word(data, 117, 0); Checksum(data);
        Check(AtaIdentifyParser.Parse(data).LogicalSectorBytes is null, "Invalid long logical-sector size withheld");
        data = SyntheticIdentify(); Word(data, 103, 1); Checksum(data);
        Check(AtaIdentifyParser.Parse(data).LogicalSectors is null, "Out-of-range LBA48 capacity rejected");
        Check(id.SmartSupported == true && id.SmartEnabled == true && id.NcqSupported == true && id.QueueDepth == 32 && id.TrimSupported == true && id.RotationRate == 7200 && id.FormFactor == "3.5 inch", "ATA feature flags, queue depth, rotating media and form factor");
        data = SyntheticIdentify(); Word(data, 83, 0); Word(data, 87, 0); Word(data, 76, 0xFFFF); Word(data, 169, 0xFFFF); Word(data, 217, 2); Word(data, 168, 0xFFFF); Checksum(data);
        var reserved = AtaIdentifyParser.Parse(data);
        Check(reserved.SmartSupported is null && reserved.SmartEnabled is null && reserved.NcqSupported is null && reserved.TrimSupported is null && reserved.RotationRate is null && reserved.FormFactor.Contains("reserved"), "Reserved/invalid IDENTIFY features remain unknown");
        data = SyntheticIdentify(); Word(data, 217, 1); Checksum(data);
        Check(AtaIdentifyParser.Parse(data).RotationRate == 1 && AtaIdentifyParser.Parse(SyntheticIdentify(false)).SmartEnabled == false, "Non-rotating indication and disabled SMART parsed separately");
        Check(Invalid(() => AtaIdentifyParser.Parse(new byte[511])) && Invalid(() => AtaIdentifyParser.Parse(new byte[512])) && Invalid(() => AtaIdentifyParser.Parse(Enumerable.Repeat((byte)255, 512).ToArray())), "Short, zeroed and all-FF IDENTIFY rejected");
        data = SyntheticIdentify(); data[50] ^= 1;
        Check(Invalid(() => AtaIdentifyParser.Parse(data)), "IDENTIFY checksum corruption rejected when A5 signature present");
        data = SyntheticIdentify(); Word(data, 0, 0x8000); Checksum(data);
        Check(Invalid(() => AtaIdentifyParser.Parse(data)), "ATAPI response cannot become SATA ATA identity");
        data = SyntheticIdentify(); data[510] = 0; data[511] = 0;
        Check(AtaIdentifyParser.Parse(data).Integrity.Contains("not supplied"), "Missing optional IDENTIFY checksum disclosed, not claimed verified");

        var attrs = SmartDataParser.Parse(SyntheticSmart(), SmartThresholdParser.Parse(SyntheticSmart(true)));
        Check(attrs.Entries[0].Threshold == 20 && attrs.Entries[1].Threshold == 10, "Reordered SMART thresholds match by ID, not row");
        Check(attrs.Entries[0].RawValue == "4660 (LE48)" && attrs.Entries[0].RawBytes.SequenceEqual(new byte[] { 0x34, 0x12, 0, 0, 0, 0 }) && attrs.Entries[0].Flags == 1, "SMART raw bytes, flags and explicit unsigned LE48 display retained");
        Check(attrs.Entries.All(a => a.Name.StartsWith("Attribute") && a.InterpretationSource == "Unknown vendor interpretation"), "Unknown F1/F2 stay visible without LBA, temperature or life inference");
        Check(SmartDataParser.Parse(SyntheticSmart(), null).Entries.All(a => a.Threshold is null), "Missing thresholds do not become zero");
        data = SyntheticSmart(true); data[14] = data[2]; Checksum(data);
        var thresholds = SmartThresholdParser.Parse(data);
        Check(thresholds.Warnings.Any() && SmartDataParser.Parse(SyntheticSmart(), thresholds).Entries.All(a => a.Threshold is null), "Duplicate threshold IDs reported and matching withheld");
        data = SyntheticSmart(); data[14] = data[2]; Checksum(data);
        var duplicate = SmartDataParser.Parse(data, SmartThresholdParser.Parse(SyntheticSmart(true)));
        Check(duplicate.Entries.Count == 2 && duplicate.Entries.All(a => a.Status == "Duplicate ID" && a.Threshold is null), "Duplicate SMART IDs retained as distinct slots without ambiguous threshold");
        data = SyntheticSmart(); data[2] = 0; Checksum(data);
        Check(SmartDataParser.Parse(data, null).Warnings.Any(), "Nonzero attribute record with missing ID reported");
        data = SyntheticSmart(); Array.Fill(data, (byte)255, 2, 12); Checksum(data);
        Check(SmartDataParser.Parse(data, null).Entries[0].Status.Contains("Invalid"), "Malformed all-FF SMART slot flagged and preserved");
        data = SyntheticSmart(); data[8] ^= 1;
        Check(Invalid(() => SmartDataParser.Parse(data, null)) && Invalid(() => SmartThresholdParser.Parse(data)), "SMART data and threshold checksums validated");
        Check(Invalid(() => SmartDataParser.Parse(new byte[512], null)) && Invalid(() => SmartThresholdParser.Parse(new byte[10])), "Empty/short SMART structures rejected");
        var returned = SyntheticResponse(AtaOperation.SmartStatus);
        Check(SmartReturnStatus.Interpret(returned) == true, "SMART RETURN STATUS C24F means threshold not exceeded only");
        returned.Registers[3] = 0xF4; returned.Registers[4] = 0x2C;
        Check(SmartReturnStatus.Interpret(returned) == false, "SMART RETURN STATUS 2CF4 means threshold exceeded");
        returned.Registers[3] = 0;
        Check(SmartReturnStatus.Interpret(returned) is null && SmartReturnStatus.Interpret(returned with { Registers = [] }) is null, "Unknown or missing SMART task file never becomes Healthy");

        Check(Marshal.SizeOf<AtaPassThroughHeader>() == 48 && Marshal.OffsetOf<AtaPassThroughHeader>(nameof(AtaPassThroughHeader.DataOffset)).ToInt32() == 24 && Marshal.OffsetOf<AtaPassThroughHeader>(nameof(AtaPassThroughHeader.Current)).ToInt32() == 40, "ATA_PASS_THROUGH_EX x64 size and pointer/task-file offsets verified");
        Check(Enum.GetValues<AtaOperation>().All(op => { var bytes = AtaCommandPolicy.Request(op); return bytes[46] == (op == AtaOperation.Identify ? 0xEC : 0xB0) && bytes.Length == 48 + AtaCommandPolicy.TransferLength(op) && (bytes[2] & 4) == 0; }), "Four allowlisted requests have exact opcode, direction and transfer size");
        var request = AtaCommandPolicy.Request(AtaOperation.SmartData); request[40] = 0xD8;
        Check(Invalid(() => AtaCommandPolicy.ValidateRequest(AtaOperation.SmartData, request)), "SMART ENABLE rejected by exact allowlist");
        request = AtaCommandPolicy.Request(AtaOperation.Identify); request[46] = 0x30;
        Check(Invalid(() => AtaCommandPolicy.ValidateRequest(AtaOperation.Identify, request)), "Sector write rejected by exact allowlist");
        request = AtaCommandPolicy.Request(AtaOperation.SmartData); request[2] |= 4;
        Check(Invalid(() => AtaCommandPolicy.ValidateRequest(AtaOperation.SmartData, request)), "Data-out flag rejected even for an allowed opcode");
        request = AtaCommandPolicy.Request(AtaOperation.Identify); Put(request, 8, 1024);
        Check(Invalid(() => AtaCommandPolicy.ValidateRequest(AtaOperation.Identify, request)), "Non-allowlisted transfer length rejected");
        var buffer = AtaCommandPolicy.Request(AtaOperation.Identify); buffer[46] = 0x50; SyntheticIdentify().CopyTo(buffer, 48);
        Check(AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, buffer, 560, true, 0).Succeeded, "Valid buffered ATA response accepted");
        buffer[46] = 0x51; buffer[40] = 4;
        Check(AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, buffer, 560, true, 0).Outcome == AtaOutcome.Failed, "DeviceIoControl success with ATA ERR/ABRT is not command success");
        buffer[46] = 0x50; Put(buffer, 24, uint.MaxValue);
        Check(AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, buffer, 560, true, 0).Outcome == AtaOutcome.InvalidResponse, "Returned invalid payload offset rejected");
        buffer = AtaCommandPolicy.Request(AtaOperation.Identify); buffer[46] = 0x50; Put(buffer, 8, 511);
        Check(AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, buffer, 559, true, 0).Outcome == AtaOutcome.InvalidResponse, "Short transfer/returned length rejected");
        Check(AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, buffer, uint.MaxValue, true, 0).Outcome == AtaOutcome.InvalidResponse, "Oversized native returned length cannot allocate or slice out of bounds");
        Check(AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, buffer, 0, false, 5).Outcome == AtaOutcome.AccessDenied && AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, buffer, 0, false, 50).Outcome == AtaOutcome.Unsupported && AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, buffer, 0, false, 1167).Outcome == AtaOutcome.Disconnected && AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, buffer, 0, false, 1117).Outcome == AtaOutcome.Failed, "Win32 denial/unsupported/disconnected/unexplained failure stay distinct");
        bool mismatch = false;
        try { SataIdentityGuard.Validate(SyntheticSata(), SyntheticSata() with { Serial = "REPLACEMENT" }); } catch (AtaTransportException e) { mismatch = e.Outcome == AtaOutcome.IdentityMismatch; }
        Check(mismatch, "Disk-number reuse with different identity rejected before ATA dispatch");
        mismatch = false;
        try { SataIdentityGuard.Validate(SyntheticSata(), SyntheticSata() with { DiskNumber = 9 }); } catch (AtaTransportException e) { mismatch = e.Outcome == AtaOutcome.IdentityMismatch; }
        Check(mismatch, "Changed disk number requires fresh discovery before diagnostic use");
    }

    private static async Task SataIntegrationTests()
    {
        var transport = new SyntheticTransport(); var provider = new WindowsSataDiagnosticsProvider(transport);
        var result = await provider.ReadAsync(SyntheticSata(), default);
        Check(transport.Operations.SequenceEqual(Enum.GetValues<AtaOperation>()) && result.Attributes.Count == 2, "SATA provider runs only four allowlisted operations and maps attributes");
        Check(result.Temperature.Value is null && result.Wear.Value is null && result.SmartPassed.Value == true, "SMART-reported status does not fabricate temperature or wear");
        transport = new() { Disabled = true }; result = await new WindowsSataDiagnosticsProvider(transport).ReadAsync(SyntheticSata(), default);
        Check(transport.Operations.SequenceEqual(new[] { AtaOperation.Identify }) && result.Commands.Count(c => c.Outcome == AtaOutcome.SmartDisabled) == 3, "Disabled SMART prevents SMART commands; never enabled automatically");
        transport = new() { OpenError = AtaOutcome.AccessDenied }; result = await new WindowsSataDiagnosticsProvider(transport).ReadAsync(SyntheticSata(), default);
        Check(result.Commands.All(c => c.Outcome == AtaOutcome.AccessDenied) && transport.Operations.Count == 0, "Access-denied open reports outcomes without ATA commands");
        transport = new() { CommandError = AtaOutcome.Disconnected }; result = await new WindowsSataDiagnosticsProvider(transport).ReadAsync(SyntheticSata(), default);
        Check(result.Identity is null && result.Commands[0].Outcome == AtaOutcome.Disconnected, "Device removal prevents publishing an identity");
        transport = new(); provider = new(transport);
        bool refusedNvme = false, refusedUnknown = false;
        try { await provider.ReadAsync(Drive("nvme", StorageProtocol.Nvme), default); } catch (AtaTransportException) { refusedNvme = true; }
        try { await provider.ReadAsync(Drive("usb", StorageProtocol.Unknown), default); } catch (AtaTransportException) { refusedUnknown = true; }
        Check(refusedNvme && refusedUnknown && transport.Opens == 0, "NVMe and unknown devices rejected before transport open");
        result = await provider.ReadAsync(SyntheticSata() with { Model = "Windows source differs", CapacityBytes = 1 }, default);
        Check(result.Discrepancies.Contains("model differs") && result.Discrepancies.Contains("capacity differs") && result.Identity?.CapacityBytes == 512_000_000, "Windows and ATA discrepancies retained without overwriting either source");

        var gate = new StorageOperationGate(); var discovery = new ControlledDiscovery();
        var blocked = new BlockingSataProvider();
        var shell = new ShellViewModel(new DiscoveryCoordinator(discovery, gate), new SataOperationCoordinator(blocked, gate));
        var scan = shell.ScanAsync(); discovery.Completion.SetResult([SyntheticSata(), SyntheticSata("sata-b")]); await scan;
        var read = shell.ReadSataAsync(); await blocked.Started.Task;
        Check(!shell.ScanCommand.CanExecute(null) && !shell.ReadSataCommand.CanExecute(null), "Diagnostics prevent discovery and duplicate diagnostics while native worker is in flight");
        bool excluded = false;
        try { await new DiscoveryCoordinator(discovery, gate).DiscoverAsync(default); } catch (InvalidOperationException) { excluded = true; }
        Check(excluded, "Shared operation gate rejects conflicting discovery independently of UI");
        shell.SelectedSata = shell.SataDevices.Single(d => d.Id == "sata-b");
        blocked.Completion.SetResult(result with { DeviceId = "sata-a" }); await read;
        Check(shell.Sata.Attributes.Count == 0 && shell.Sata.Device?.Id == "sata-b" && shell.SataOperationStatus.Contains("cancelled"), "Selection change cancels and rejects late previous-device SATA results");
        blocked = new(); shell = new(new DiscoveryCoordinator(discovery, gate), new SataOperationCoordinator(blocked, gate));
        await shell.ScanAsync(); read = shell.ReadSataAsync(); await blocked.Started.Task;
        shell.Cancel();
        Check(shell.SataBusy && !shell.ScanCommand.CanExecute(null), "Cancelled SATA call holds worker gate until it actually completes");
        blocked.Completion.SetResult(result); await read;
        Check(shell.Sata.Attributes.Count == 0 && shell.SataOperationStatus.Contains("Partial data discarded"), "Cancelled partial SATA results are never labelled complete");
        blocked = new(); shell = new(new DiscoveryCoordinator(discovery, gate), new SataOperationCoordinator(blocked, gate));
        await shell.ScanAsync(); read = shell.ReadSataAsync(); await blocked.Started.Task;
        shell.Close(); blocked.Completion.SetResult(result); await read;
        Check(shell.Sata.Attributes.Count == 0 && !shell.ReadSataCommand.CanExecute(null), "Application close rejects late SATA results");
    }
    private sealed class SyntheticTransport : IAtaTransport, IAtaSession
    {
        public int Opens;
        public bool Disabled;
        public AtaOutcome? OpenError, CommandError;
        public List<AtaOperation> Operations { get; } = [];
        public IAtaSession Open(StorageDevice device, CancellationToken token)
        { Opens++; if (OpenError is AtaOutcome error) throw new AtaTransportException(error, "Synthetic open failure"); return this; }
        public AtaCommandResult Execute(AtaOperation operation, CancellationToken token)
        {
            Operations.Add(operation);
            return CommandError is AtaOutcome error ? AtaCommandResult.Missing(operation, error, "Synthetic command failure") :
                SyntheticResponse(operation, operation == AtaOperation.Identify ? SyntheticIdentify(!Disabled) : null);
        }
        public void Dispose() { }
    }
    private sealed class BlockingSataProvider : ISataDiagnosticsProvider
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SataDiagnostics> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SataDiagnostics> ReadAsync(StorageDevice device, CancellationToken token, IProgress<string>? progress = null)
        { Started.SetResult(); return Completion.Task; }
    }
}
