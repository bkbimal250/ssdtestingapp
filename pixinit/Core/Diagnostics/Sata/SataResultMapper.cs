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
        string Serial(string? value) => string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
        string ata = identity is null ? "ATA identity unavailable; see command outcomes." :
            $"ATA model: {identity.Model ?? "Unavailable"}\nATA serial: {Serial(identity.Serial)}\nATA firmware: {identity.Firmware ?? "Unavailable"}\n" +
            $"ATA capacity: {identity.CapacityBytes?.ToString("N0") ?? "Unavailable"} bytes; logical sectors: {identity.LogicalSectors?.ToString() ?? "Unavailable"}\n" +
            $"Logical sector: {identity.LogicalSectorBytes?.ToString() ?? "Unavailable"} bytes; physical sector: {identity.PhysicalSectorBytes?.ToString() ?? "Unavailable"} bytes\n" +
            $"LBA: {Flag(identity.LbaSupported)}; LBA48: {Flag(identity.Lba48Supported)}\nATA versions: {identity.AtaVersions}\n" +
            $"SMART supported: {Flag(identity.SmartSupported)}; enabled: {Flag(identity.SmartEnabled)}\nDrive TRIM support: {Flag(identity.TrimSupported)} (Windows TRIM configuration not queried)\n" +
            $"NCQ: {Flag(identity.NcqSupported)}; queue depth: {identity.QueueDepth?.ToString() ?? "Unavailable"}\n" +
            $"Rotation: {(identity.RotationRate == 1 ? "Non-rotating media indication" : identity.RotationRate is ushort rpm ? $"{rpm} RPM" : "Unspecified / reserved")}\nForm factor: {identity.FormFactor}\n" +
            $"Read-only security flags: {(identity.SecurityStatus is ushort security ? $"0x{security:X4}; supported={(security & 1) != 0}, enabled={(security & 2) != 0}, locked={(security & 4) != 0}, frozen={(security & 8) != 0}" : "Unavailable")}\n" +
            $"{identity.Integrity}\n{string.Join(" ", identity.Limitations)}";
        var warnings = (attributes?.Warnings ?? []).Concat(thresholds?.Warnings ?? []).ToArray();
        var dataResponse = commands.FirstOrDefault(x => x.Operation == AtaOperation.SmartData && x.Succeeded);
        ulong? Raw(byte id)
        {
            var rows = attributes?.Entries.Where(x => x.Id == id).ToArray();
            if (dataResponse is null || rows?.Length != 1 || rows[0].RawBytes.Length != 6 || rows[0].RawBytes.All(x => x == 255)) return null;
            ulong raw = 0; for (int i = 0; i < 6; i++) raw |= (ulong)rows[0].RawBytes[i] << (8 * i);
            return raw;
        }
        var raw194 = Raw(194); double? temp194 = raw194 is ulong rawTemp && (rawTemp & 255) is > 0 and < 128 ? rawTemp & 255 : null;
        var temp = temp194 is double t ? new Metric<double>(t, "°C", Availability.Available,
            "SMART 194 raw low byte; conventional encoding, vendor interpretation unverified", dataResponse!.ObservedAt) : Metric<double>.Missing("°C");
        var written = Raw(241);
        ata += $"\nPower-on hours (SMART 9 assumed LE48 hours): {Raw(9)?.ToString() ?? "Unavailable"}; vendor units unverified.";
        return new(device.Id, smart, temp, Metric<double>.Missing("%"), attributes?.Entries ?? [],
            "Unavailable: no verified vendor/model interpretation. SMART 194/241/9 conversions use conventional encodings and remain unverified for this vendor.", ata,
            $"SATA capability word: {(identity?.SataCapabilities is ushort caps ? $"0x{caps:X4} (supported features only)" : "Unavailable")}\nNegotiated SATA speed: unavailable; maximum capability is not current speed.")
        {
            Tbw = new(written is ulong lbas ? lbas * 512d / 1e12 : null, InterpretationVerified: false,
                Source: "SMART 241 assumed LE48 count of 512-byte LBAs; vendor encoding and units unverified"),
            Thermal = ThermalInfo.FromMetric(temp, false),
            PowerOnHours = Raw(9) is ulong hours ? hours : null,
            Identity = identity, Commands = commands.ToArray(), Discrepancies = string.Join("\n", discrepancies),
            StatusExplanation = passed is bool p ? p ? "Threshold not exceeded" : "Threshold exceeded" : response is null ? "Not queried" : $"{response.Outcome}: {response.Explanation}",
            Summary = $"Read sequence finished · {commands.Count(c => c.Succeeded)}/{commands.Count} valid command responses. No overall health/QC result. " + string.Join(" ", warnings)
        };
    }
}
