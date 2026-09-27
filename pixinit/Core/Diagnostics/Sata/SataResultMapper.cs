using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;

namespace pixinit.Core.Diagnostics.Sata;

public static class SataResultMapper
{
    public static SataDiagnostics Map(StorageDevice device, AtaIdentity? identity, SmartAttributes? attributes,
        SmartThresholds? thresholds, IReadOnlyList<AtaCommandResult> commands, IReadOnlyList<string> discrepancies)
    {
        var response = commands.FirstOrDefault(c => c.Operation == AtaOperation.SmartStatus);
        bool? passed = response is null ? null : SmartReturnStatus.Interpret(response);
        var smart = passed is bool value ? new Metric<bool>(value, "", Availability.Available, "ATA SMART RETURN STATUS", response!.ObservedAt,
            "Device-reported threshold status only; not an overall health or QC assessment") : Metric<bool>.Missing();
        string Flag(bool? value) => value is null ? "Unknown" : value.Value ? "Yes" : "No";
        string Mask(string? value) => string.IsNullOrWhiteSpace(value) ? "Unavailable" : value.Length > 4 ? "•••• " + value[^4..] : "••••";
        string ata = identity is null ? "ATA identity unavailable; see command outcomes." :
            $"ATA model: {identity.Model ?? "Unavailable"}\nATA serial: {Mask(identity.Serial)}\nATA firmware: {identity.Firmware ?? "Unavailable"}\n" +
            $"ATA capacity: {identity.CapacityBytes?.ToString("N0") ?? "Unavailable"} bytes; logical sectors: {identity.LogicalSectors?.ToString() ?? "Unavailable"}\n" +
            $"Logical sector: {identity.LogicalSectorBytes?.ToString() ?? "Unavailable"} bytes; physical sector: {identity.PhysicalSectorBytes?.ToString() ?? "Unavailable"} bytes\n" +
            $"LBA: {Flag(identity.LbaSupported)}; LBA48: {Flag(identity.Lba48Supported)}\nATA versions: {identity.AtaVersions}\n" +
            $"SMART supported: {Flag(identity.SmartSupported)}; enabled: {Flag(identity.SmartEnabled)}\nDrive TRIM support: {Flag(identity.TrimSupported)} (Windows TRIM configuration not queried)\n" +
            $"NCQ: {Flag(identity.NcqSupported)}; queue depth: {identity.QueueDepth?.ToString() ?? "Unavailable"}\n" +
            $"Rotation: {(identity.RotationRate == 1 ? "Non-rotating media indication" : identity.RotationRate is ushort rpm ? $"{rpm} RPM" : "Unspecified / reserved")}\nForm factor: {identity.FormFactor}\n" +
            $"Read-only security flags: {(identity.SecurityStatus is ushort security ? $"0x{security:X4}; supported={(security & 1) != 0}, enabled={(security & 2) != 0}, locked={(security & 4) != 0}, frozen={(security & 8) != 0}" : "Unavailable")}\n" +
            $"{identity.Integrity}\n{string.Join(" ", identity.Limitations)}";
        var warnings = (attributes?.Warnings ?? []).Concat(thresholds?.Warnings ?? []).ToArray();
        return new(device.Id, smart, Metric<double>.Missing("°C"), Metric<double>.Missing("%"), attributes?.Entries ?? [],
            "Unavailable: no verified vendor/model interpretation. No temperature, wear or endurance units inferred.", ata,
            $"SATA capability word: {(identity?.SataCapabilities is ushort caps ? $"0x{caps:X4} (supported features only)" : "Unavailable")}\nNegotiated SATA speed: unavailable; maximum capability is not current speed.")
        {
            Identity = identity, Commands = commands.ToArray(), Discrepancies = string.Join("\n", discrepancies),
            StatusExplanation = passed is bool p ? p ? "Threshold not exceeded" : "Threshold exceeded" : response is null ? "Not queried" : $"{response.Outcome}: {response.Explanation}",
            Summary = $"Read sequence finished · {commands.Count(c => c.Succeeded)}/{commands.Count} valid command responses. No overall health/QC result. " + string.Join(" ", warnings)
        };
    }
}
