using System.IO;
using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Assessment;
using pixinit.Infrastructure.Logging;
using pixinit.Infrastructure.Windows.Discovery;

namespace pixinit.Infrastructure.Windows.Sata;

public sealed class WindowsSataDiagnosticsProvider : ISataDiagnosticsProvider
{
    private readonly IAtaTransport transport;
    private readonly Func<StorageDevice, CancellationToken, byte[]?> readWmiSmart;
    public WindowsSataDiagnosticsProvider() : this(new WindowsAtaTransport()) { }
    internal WindowsSataDiagnosticsProvider(IAtaTransport transport, Func<StorageDevice, CancellationToken, byte[]?>? readWmiSmart = null)
    {
        this.transport = transport;
        this.readWmiSmart = readWmiSmart ?? RaidDisplayFallback.ReadFailurePredictSmart;
    }
    public Task<SataDiagnostics> ReadAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
        Task.FromResult(Read(device, cancellationToken, progress));

    private SataDiagnostics Read(StorageDevice device, CancellationToken token, IProgress<string>? progress)
    {
        SataIdentityGuard.RequireSata(device); // reject before even constructing a transport session
        var commands = new List<AtaCommandResult>();
        AtaIdentity? identity = null; SmartThresholds? thresholds = null; SmartAttributes? attributes = null;
        var notes = new List<string>();
        void Parse(AtaCommandResult command, Action parser)
        {
            if (!command.Succeeded) return;
            try { parser(); }
            catch (InvalidDataException ex)
            {
                int index = commands.IndexOf(command);
                commands[index] = command with { Outcome = AtaOutcome.InvalidResponse, Explanation = ex.Message };
            }
        }
        try
        {
            token.ThrowIfCancellationRequested(); progress?.Report("Opening selected drive · Validating identity");
            using var session = transport.Open(device, token);
            AtaCommandResult Run(AtaOperation operation, string stage)
            {
                token.ThrowIfCancellationRequested(); progress?.Report(stage);
                var response = session.Execute(operation, token); commands.Add(response);
                string suffix = string.IsNullOrWhiteSpace(device.Serial) ? "unknown" : device.Serial[^Math.Min(4, device.Serial.Length)..];
                DiscoveryLog.Write($"SATA diagnostic: disk={device.DiskNumber}; serial ending={suffix.TrimEnd('.')}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; Protocol attempted=SATA {operation}; SMART passthrough result={response.Outcome}; error code={(response.Win32Error is int error ? $"0x{error:X8}" : "none")}; {response.Explanation}");
                token.ThrowIfCancellationRequested();
                return response;
            }
            var identify = Run(AtaOperation.Identify, "Reading identity");
            Parse(identify, () => identity = AtaIdentifyParser.Parse(identify.Payload));
            if (identity is not null)
            {
                if (!string.IsNullOrWhiteSpace(identity.Model) && !string.Equals(identity.Model.Trim(), device.Model.Trim(), StringComparison.Ordinal)) notes.Add("ATA model differs from Windows discovery; both sources retained.");
                if (!string.IsNullOrWhiteSpace(identity.Serial) && !string.IsNullOrWhiteSpace(device.Serial) && identity.Serial.Trim() != device.Serial.Trim())
                    notes.Add(device.Bus == ConnectionBus.Usb
                        ? "USB enclosure serial and ATA drive serial are distinct; both retained. USB path metadata is revalidated for each command."
                        : "ATA serial differs from Windows discovery; both sources retained; further commands withheld.");
                if (identity.Firmware is not null && device.Firmware is not null && identity.Firmware != device.Firmware) notes.Add("ATA firmware differs from Windows discovery; both sources retained.");
                if (identity.CapacityBytes is not null && device.CapacityBytes is not null && identity.CapacityBytes != device.CapacityBytes) notes.Add("ATA byte capacity differs from Windows discovery; neither source overwrites the other.");
            }
            bool usbBridge = device.Bus == ConnectionBus.Usb;
            if (identity is null || notes.Any(n => n.Contains("serial differs")))
                Skip(AtaOutcome.NotQueried, "SMART not queried because ATA identity could not be validated.");
            else if (!usbBridge && identity.SmartSupported != true)
                Skip(identity.SmartSupported == false ? AtaOutcome.Unsupported : AtaOutcome.NotQueried,
                    identity.SmartSupported == false ? "SMART is not supported according to valid IDENTIFY data." : "SMART support validity not established; commands withheld.");
            else if (!usbBridge && identity.SmartEnabled != true)
                Skip(identity.SmartEnabled == false ? AtaOutcome.SmartDisabled : AtaOutcome.NotQueried,
                    identity.SmartEnabled == false ? "SMART disabled. PIXINIT will not enable it." : "SMART enabled state unknown; commands withheld.");
            else
            {
                if (usbBridge && identity.SmartEnabled != true)
                    notes.Add("USB bridge IDENTIFY did not confirm SMART enabled. Read-only SMART commands are still attempted; SMART is not enabled by PIXINIT.");
                var data = Run(AtaOperation.SmartData, "Reading SMART attributes");
                var limits = Run(AtaOperation.SmartThresholds, "Reading thresholds");
                Parse(limits, () => thresholds = SmartThresholdParser.Parse(limits.Payload));
                Parse(data, () => attributes = SmartDataParser.Parse(data.Payload, thresholds));
                int dataIndex = commands.IndexOf(data);
                double? usageUsedPercent = ReadMountedVolumeUsedPercent(device);
                // A parsed page with zero host counters is not final. SAT, WMI and CSMI can still hold the real values.
                if (device.Protocol == StorageProtocol.Sata && ShouldProbeHostCounters(device, attributes))
                {
                    void Accept(AtaCommandResult candidate, string source)
                    {
                        if (!candidate.Succeeded) return;
                        try
                        {
                            var parsed = SmartDataParser.Parse(candidate.Payload, thresholds);
                            LogCounterBytes(device, source, parsed, usageUsedPercent);
                            if (!HasPositiveHostCounter(attributes) && (attributes is null || HasPositiveHostCounter(parsed)))
                            {
                                attributes = parsed;
                                commands[dataIndex] = candidate;
                            }
                        }
                        catch (InvalidDataException ex) { DiscoveryLog.Write($"SMART fallback parse failed: disk={device.DiskNumber}; serial ending={device.SerialSuffix}; fallback source={source}; SMART passthrough result=invalid data; error code=none", ex); }
                    }

                    // Probe in order: SCSI SAT, Windows WMI snapshot, then documented Windows
                    // protocol-specific query. Stop once a valid SMART response is available.
                    var satResult = session.ExecuteSatSmartData(token);
                    DiscoveryLog.Write($"Probe b SCSI SAT: disk={device.DiskNumber}; serial ending={SerialEnding(device.Serial)}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; Protocol attempted=SCSI SAT ATA SMART READ DATA (0x85/0xD0); SMART passthrough result={satResult.Outcome}; error code={(satResult.Win32Error is int satError ? $"0x{satError:X8}" : "none")}; {satResult.Explanation}");
                    Accept(satResult, "SCSI SAT");
                    if (attributes is null)
                    {
                        byte[]? wmiData = readWmiSmart(device, token);
                        DiscoveryLog.Write($"Probe c MSStorageDriver_FailurePredictData: disk={device.DiskNumber}; serial ending={SerialEnding(device.Serial)}; result={(wmiData is null ? "unmatched" : "512-byte VendorSpecific, attributes at offset 2")}.");
                        if (wmiData is not null) Accept(new(AtaOperation.SmartData, AtaOutcome.Success,
                            "SMART attributes returned by MSStorageDriver_FailurePredictData via Windows WMI. VendorSpecific attributes start at offset 2, 12 bytes each.", wmiData, [], DateTimeOffset.UtcNow), "MSStorageDriver_FailurePredictData");
                    }
                    if (attributes is null)
                    {
                        var protocolResult = session.ExecuteProtocolSpecificSmartData(token);
                        DiscoveryLog.Write($"RAID ATA protocol SMART fallback: disk={device.DiskNumber}; serial ending={SerialEnding(device.Serial)}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; Protocol attempted=IOCTL_STORAGE_QUERY_PROPERTY StorageDeviceProtocolSpecificProperty ATA LogPage SMART READ DATA; SMART passthrough result={protocolResult.Outcome}; error code={(protocolResult.Win32Error is int protocolError ? $"0x{protocolError:X8}" : "none")}; {protocolResult.Explanation}");
                        Accept(protocolResult, "IOCTL_STORAGE_QUERY_PROPERTY ATA LogPage");
                    }
                    if (!HasPositiveHostCounter(attributes))
                    {
                        byte[]? csmi = IntelRstSmart.TryReadSmart(device, token);
                        if (csmi is not null) Accept(new(AtaOperation.SmartData, AtaOutcome.Success,
                            "SMART READ DATA returned by published CSMI STP passthrough on IOCTL_SCSI_MINIPORT.", csmi, [], DateTimeOffset.UtcNow), "IOCTL_SCSI_MINIPORT CSMI");
                        else DiscoveryLog.Write($"Probe d IOCTL_SCSI_MINIPORT: disk={device.DiskNumber}; serial ending={SerialEnding(device.Serial)}; SMART passthrough result=unavailable.");
                        if (!HasPositiveHostCounter(attributes))
                        {
                            RaidDisplayFallback.LogPhysicalDiskHealth(device);
                            notes.Add(device.Bus == ConnectionBus.Raid
                                ? "Unavailable - RAID blocks host counters. ATA, SAT, WMI and CSMI did not return a non-zero SMART 241/242 value."
                                : $"Host writes/reads stayed unavailable. Windows Storage health status: {device.WindowsHealthStatus ?? "unavailable"}.");
                        }
                    }
                }
                var status = Run(AtaOperation.SmartStatus, "Reading SMART status");
                if (status.Succeeded && SmartReturnStatus.Interpret(status) is null)
                    commands[^1] = status with { Outcome = AtaOutcome.InvalidResponse, Explanation = "Unrecognized SMART RETURN STATUS task-file response; status is Unknown." };
            }
        }
        catch (AtaTransportException ex) { Skip(ex.Outcome, ex.Message); DiscoveryLog.Write("SATA transport stopped", ex); }
        if (device.Bus == ConnectionBus.Raid && device.ProtocolHeuristic && attributes is null &&
            !notes.Any(note => note.Contains("serial differs", StringComparison.Ordinal)))
        {
            token.ThrowIfCancellationRequested();
            byte[]? snapshot = readWmiSmart(device, token);
            if (snapshot is not null)
            {
                try
                {
                    var parsed = SmartDataParser.Parse(snapshot, null);
                    attributes = parsed;
                    commands.RemoveAll(command => command.Operation == AtaOperation.SmartData && !command.Succeeded);
                    commands.Add(new(AtaOperation.SmartData, AtaOutcome.Success,
                        "SMART snapshot from an exactly matched Windows WMI instance; ATA identity remains unavailable.",
                        snapshot, [], DateTimeOffset.UtcNow));
                    LogCounterBytes(device, "MSStorageDriver_FailurePredictData without ATA Identify", parsed, ReadMountedVolumeUsedPercent(device));
                    notes.Add("ATA Identify unavailable; an independently matched Windows SMART snapshot supplied raw attributes.");
                }
                catch (InvalidDataException ex)
                {
                    DiscoveryLog.Write($"RAID WMI SMART snapshot invalid: disk={device.DiskNumber}; serial ending={device.SerialSuffix}; fallback source=MSStorageDriver_FailurePredictData", ex);
                }
            }
        }
        token.ThrowIfCancellationRequested(); progress?.Report("Preparing results");
        double? usage = ReadMountedVolumeUsedPercent(device);
        var mapped = SataResultMapper.Map(device, identity, attributes, thresholds, commands, notes, usage);
        if (mapped.Tbw?.WrittenTB is 0d && (device.Bus == ConnectionBus.Raid || usage is > 1))
        {
            string why = device.Bus == ConnectionBus.Raid
                ? "Unavailable - RAID blocks host counters. SMART 241 stayed zero after ATA, SAT, WMI and Intel RST."
                : "Unavailable - SMART 241 raw is zero after ATA, SAT and WMI. Host writes are not shown as 0.0 TB.";
            mapped = mapped with
            {
                Tbw = mapped.Tbw with { WrittenTB = null, InterpretationVerified = false, Source = why },
                HostReadsTB = mapped.HostReadsTB is 0d ? null : mapped.HostReadsTB,
                Discrepancies = string.Join("\n", new[] { mapped.Discrepancies, why }.Where(line => !string.IsNullOrWhiteSpace(line)))
            };
        }
        DiscoveryLog.Write($"SATA host-counter result: disk={device.DiskNumber}; serial ending={device.SerialSuffix}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; SMART 241 raw bytes={RawBytes(attributes, 241)}; SMART 242 raw bytes={RawBytes(attributes, 242)}; host writes TB={(mapped.Tbw?.WrittenTB is double writes ? writes.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) : "unavailable")}; host reads TB={(mapped.HostReadsTB is double reads ? reads.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) : "unavailable")}; TBW reference={(mapped.Tbw is { RatedTB: > 0 } finalRating ? $"{finalRating.RatedTB:F0} TB, unverified" : "none")}; source={commands.FirstOrDefault(command => command.Operation == AtaOperation.SmartData && command.Succeeded)?.Explanation ?? "unavailable"}.");
        var assessment = AssessmentPolicies.Assess(mapped);
        string summary = $"Read sequence finished · {commands.Count(c => c.Succeeded)}/{commands.Count} valid command responses · Assessment {assessment.StateDisplay} · Checklist {assessment.ChecklistDisplay}";
        if (device.Bus == ConnectionBus.Usb && attributes is null)
            summary = "USB SATA diagnostics unavailable · " + (commands.FirstOrDefault(c => !c.Succeeded)?.Explanation ?? "Bridge did not expose SMART data.") + " · Refresh to retry.";
        return mapped with { Assessment = assessment, Summary = summary };

        void Skip(AtaOutcome outcome, string reason)
        {
            foreach (var op in Enum.GetValues<AtaOperation>().Where(op => commands.All(c => c.Operation != op)))
                commands.Add(AtaCommandResult.Missing(op, outcome, reason));
        }
    }

    private static bool HasPositiveHostCounter(SmartAttributes? attributes) =>
        SataHostCounters.Decode(attributes, attributes is not null) is { WritesTB: > 0 } or { ReadsTB: > 0 };
    private static bool ShouldProbeHostCounters(StorageDevice device, SmartAttributes? attributes)
    {
        if (device.Bus == ConnectionBus.Usb) return false;
        if (attributes is null) return true;
        var decoded = SataHostCounters.Decode(attributes, true);
        if (decoded is null) return device.Bus == ConnectionBus.Raid || device.ProtocolHeuristic;
        return decoded.WritesTB is not > 0 && decoded.ReadsTB is not > 0;
    }
    private static ulong? RawCounter(SmartAttributes? attributes, byte id)
    {
        var rows = attributes?.Entries.Where(row => row.Id == id).ToArray();
        if (rows?.Length != 1 || rows[0].RawBytes.Length != 6 || rows[0].RawBytes.All(value => value == 255)) return null;
        ulong raw = 0;
        for (int i = 0; i < 6; i++) raw |= (ulong)rows[0].RawBytes[i] << (8 * i);
        return raw;
    }
    private static string RawBytes(SmartAttributes? attributes, byte id)
    {
        var rows = attributes?.Entries.Where(row => row.Id == id).ToArray();
        return rows?.Length == 1 && rows[0].RawBytes.Length == 6 ? Convert.ToHexString(rows[0].RawBytes) : "unavailable";
    }
    private static string SerialEnding(string? serial)
    {
        string value = (serial ?? "").Trim().TrimEnd('.');
        return value.Length == 0 ? "unknown" : value[^Math.Min(4, value.Length)..];
    }
    private static void LogCounterBytes(StorageDevice device, string source, SmartAttributes attributes, double? usedPercent)
    {
        ulong? writes = RawCounter(attributes, 241), reads = RawCounter(attributes, 242);
        DiscoveryLog.Write($"SATA SMART fallback counters: disk={device.DiskNumber}; serial ending={device.SerialSuffix}; fallback source={source}; volume used={(usedPercent is double value ? $"{value:F1}%" : "unavailable")}; SMART 241 raw={(writes is ulong w ? w.ToString("X12") : "unavailable")}; SMART 242 raw={(reads is ulong r ? r.ToString("X12") : "unavailable")}; SMART passthrough result={(writes is null && reads is null ? "attributes absent" : "attributes parsed")}; error code=none.");
    }
    private static double? ReadMountedVolumeUsedPercent(StorageDevice device)
    {
        foreach (string mount in device.MountPoints)
        {
            if (mount.Length is not (2 or 3) || !char.IsAsciiLetter(mount[0]) || mount[1] != ':') continue;
            try
            {
                var volume = new DriveInfo(mount[..2]);
                if (!volume.IsReady || volume.TotalSize <= 0) continue;
                return 100d * (volume.TotalSize - volume.AvailableFreeSpace) / volume.TotalSize;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }
}
