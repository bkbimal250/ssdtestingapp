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
    public WindowsSataDiagnosticsProvider() : this(new WindowsAtaTransport()) { }
    internal WindowsSataDiagnosticsProvider(IAtaTransport transport) => this.transport = transport;
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
                if (!string.IsNullOrWhiteSpace(identity.Serial) && !string.IsNullOrWhiteSpace(device.Serial) && identity.Serial.Trim() != device.Serial.Trim()) notes.Add("ATA serial differs from Windows discovery; both sources retained; further commands withheld.");
                if (identity.Firmware is not null && device.Firmware is not null && identity.Firmware != device.Firmware) notes.Add("ATA firmware differs from Windows discovery; both sources retained.");
                if (identity.CapacityBytes is not null && device.CapacityBytes is not null && identity.CapacityBytes != device.CapacityBytes) notes.Add("ATA byte capacity differs from Windows discovery; neither source overwrites the other.");
            }
            if (identity is null || notes.Any(n => n.Contains("serial differs")))
                Skip(AtaOutcome.NotQueried, "SMART not queried because ATA identity could not be validated.");
            else if (identity.SmartSupported != true)
                Skip(identity.SmartSupported == false ? AtaOutcome.Unsupported : AtaOutcome.NotQueried,
                    identity.SmartSupported == false ? "SMART is not supported according to valid IDENTIFY data." : "SMART support validity not established; commands withheld.");
            else if (identity.SmartEnabled != true)
                Skip(identity.SmartEnabled == false ? AtaOutcome.SmartDisabled : AtaOutcome.NotQueried,
                    identity.SmartEnabled == false ? "SMART disabled. PIXINIT will not enable it." : "SMART enabled state unknown; commands withheld.");
            else
            {
                var data = Run(AtaOperation.SmartData, "Reading SMART attributes");
                var limits = Run(AtaOperation.SmartThresholds, "Reading thresholds");
                Parse(limits, () => thresholds = SmartThresholdParser.Parse(limits.Payload));
                Parse(data, () => attributes = SmartDataParser.Parse(data.Payload, thresholds));
                int dataIndex = commands.IndexOf(data);
                double? usageUsedPercent = ReadMountedVolumeUsedPercent(device);
                bool CounterLooksZero(SmartAttributes? source, byte id) => RawCounter(source, id) is not ulong raw || raw * 512d / 1e12 < 0.0005;
                bool needsCounterFallback = attributes is null || CounterLooksZero(attributes, 241) || CounterLooksZero(attributes, 242);
                if (needsCounterFallback && device.Protocol == StorageProtocol.Sata)
                {
                    int CounterScore(SmartAttributes? source) =>
                        (RawCounter(source, 241) is ulong writes && (usageUsedPercent is not > 10 || writes * 512d / 1e12 >= 0.0005) ? 1 : 0) +
                        (RawCounter(source, 242) is ulong reads && (usageUsedPercent is not > 10 || reads * 512d / 1e12 >= 0.0005) ? 1 : 0);
                    bool HasUsefulCounters(SmartAttributes? source) => CounterScore(source) > 0;
                    void Accept(AtaCommandResult candidate, string source)
                    {
                        if (!candidate.Succeeded) return;
                        try
                        {
                            var parsed = SmartDataParser.Parse(candidate.Payload, thresholds);
                            LogCounterBytes(device, source, parsed, usageUsedPercent);
                            if (attributes is null || CounterScore(parsed) > CounterScore(attributes))
                            {
                                attributes = parsed;
                                commands[dataIndex] = candidate;
                            }
                        }
                        catch (InvalidDataException ex) { DiscoveryLog.Write($"RAID SMART fallback parse failed: disk={device.DiskNumber}; serial ending={device.SerialSuffix}; fallback source={source}; SMART passthrough result=invalid data; error code=none", ex); }
                    }

                    // Probe in order: SCSI SAT, Windows WMI snapshot, then documented Windows
                    // protocol-specific query. A zero counter on a substantially used volume is
                    // treated as blocked/unavailable and does not stop the fallback chain.
                    var satResult = session.ExecuteSatSmartData(token);
                    DiscoveryLog.Write($"RAID SAT SMART fallback: disk={device.DiskNumber}; serial ending={SerialEnding(device.Serial)}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; Protocol attempted=SCSI SAT ATA SMART READ DATA (0x85/0xD0); SMART passthrough result={satResult.Outcome}; error code={(satResult.Win32Error is int satError ? $"0x{satError:X8}" : "none")}; {satResult.Explanation}");
                    Accept(satResult, "SCSI SAT");
                    if (!HasUsefulCounters(attributes) || usageUsedPercent is > 10 && (CounterLooksZero(attributes, 241) || CounterLooksZero(attributes, 242)))
                    {
                        byte[]? wmiData = RaidDisplayFallback.ReadFailurePredictSmart(device, token);
                        if (wmiData is not null) Accept(new(AtaOperation.SmartData, AtaOutcome.Success,
                            "SMART attributes returned by MSStorageDriver_FailurePredictData via Windows WMI.", wmiData, [], DateTimeOffset.UtcNow), "MSStorageDriver_FailurePredictData");
                    }
                    if (!HasUsefulCounters(attributes) || usageUsedPercent is > 10 && (CounterLooksZero(attributes, 241) || CounterLooksZero(attributes, 242)))
                    {
                        var protocolResult = session.ExecuteProtocolSpecificSmartData(token);
                        DiscoveryLog.Write($"RAID ATA protocol SMART fallback: disk={device.DiskNumber}; serial ending={SerialEnding(device.Serial)}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; Protocol attempted=IOCTL_STORAGE_QUERY_PROPERTY StorageDeviceProtocolSpecificProperty ATA LogPage SMART READ DATA; SMART passthrough result={protocolResult.Outcome}; error code={(protocolResult.Win32Error is int protocolError ? $"0x{protocolError:X8}" : "none")}; {protocolResult.Explanation}");
                        Accept(protocolResult, "IOCTL_STORAGE_QUERY_PROPERTY ATA LogPage");
                    }
                    if (!HasUsefulCounters(attributes) || usageUsedPercent is > 10 && (CounterLooksZero(attributes, 241) || CounterLooksZero(attributes, 242)))
                    {
                        // Intel RST miniport SMART IOCTLs are vendor-private; no control code or
                        // request buffer contract is available in the target OS interface. Do not
                        // send a guessed request to a storage controller.
                        DiscoveryLog.Write($"RAID Intel RST SMART fallback: disk={device.DiskNumber}; serial ending={SerialEnding(device.Serial)}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; Protocol attempted=IOCTL_SCSI_MINIPORT IntelRst private request; SMART passthrough result=not dispatched; error code=none; vendor control code/buffer contract unavailable.");
                        RaidDisplayFallback.LogPhysicalDiskHealth(device);
                        notes.Add($"Host writes/reads unavailable through the available read-only SATA paths. Windows Storage health status: {device.WindowsHealthStatus ?? "unavailable"}.");
                    }
                    ulong? finalWrites = RawCounter(attributes, 241), finalReads = RawCounter(attributes, 242);
                    if (usageUsedPercent is > 10 && (finalWrites is ulong fw0 && fw0 * 512d / 1e12 < 0.0005 || finalReads is ulong fr0 && fr0 * 512d / 1e12 < 0.0005))
                    {
                        if (finalWrites is ulong writeCounter && writeCounter * 512d / 1e12 < 0.0005) finalWrites = null;
                        if (finalReads is ulong readCounter && readCounter * 512d / 1e12 < 0.0005) finalReads = null;
                    }
                    var endurance = ManufacturerEnduranceCatalog.EstimateSata(device);
                    DiscoveryLog.Write($"SATA host-counter final: disk={device.DiskNumber}; serial ending={SerialEnding(device.Serial)}; usage={(usageUsedPercent is double used ? $"{used:F1}%" : "unavailable")}; SMART 241 raw={(finalWrites is ulong w ? w.ToString("X12") : "unavailable")}; SMART 242 raw={(finalReads is ulong r ? r.ToString("X12") : "unavailable")}; Host writes TB={(finalWrites is ulong fw ? (fw * 512d / 1e12).ToString("F6", System.Globalization.CultureInfo.InvariantCulture) : "unavailable")}; Host reads TB={(finalReads is ulong fr ? (fr * 512d / 1e12).ToString("F6", System.Globalization.CultureInfo.InvariantCulture) : "unavailable")}; TBW reference={(endurance is { } rating ? $"{rating.RatedTB:F0} TB; source={rating.Source}" : "none")}; SMART passthrough result={(finalWrites is null ? "blocked/unavailable" : "counter returned")}; error code=none.");
                }
                var status = Run(AtaOperation.SmartStatus, "Reading SMART status");
                if (status.Succeeded && SmartReturnStatus.Interpret(status) is null)
                    commands[^1] = status with { Outcome = AtaOutcome.InvalidResponse, Explanation = "Unrecognized SMART RETURN STATUS task-file response; status is Unknown." };
            }
        }
        catch (AtaTransportException ex) { Skip(ex.Outcome, ex.Message); DiscoveryLog.Write("SATA transport stopped", ex); }
        token.ThrowIfCancellationRequested(); progress?.Report("Preparing results");
        var mapped = SataResultMapper.Map(device, identity, attributes, thresholds, commands, notes, ReadMountedVolumeUsedPercent(device));
        var assessment = AssessmentPolicies.Assess(mapped);
        return mapped with { Assessment = assessment, Summary = $"Read sequence finished · {commands.Count(c => c.Succeeded)}/{commands.Count} valid command responses · Assessment {assessment.StateDisplay} · Checklist {assessment.ChecklistDisplay}" };

        void Skip(AtaOutcome outcome, string reason)
        {
            foreach (var op in Enum.GetValues<AtaOperation>().Where(op => commands.All(c => c.Operation != op)))
                commands.Add(AtaCommandResult.Missing(op, outcome, reason));
        }
    }

    private static ulong? RawCounter(SmartAttributes? attributes, byte id)
    {
        var rows = attributes?.Entries.Where(row => row.Id == id).ToArray();
        if (rows?.Length != 1 || rows[0].RawBytes.Length != 6 || rows[0].RawBytes.All(value => value == 255)) return null;
        ulong raw = 0;
        for (int i = 0; i < 6; i++) raw |= (ulong)rows[0].RawBytes[i] << (8 * i);
        return raw;
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
