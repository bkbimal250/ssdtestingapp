using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;

namespace pixinit.Core.Diagnostics.Nvme;

public static class NvmeResultMapper
{
    public static NvmeDiagnostics Map(StorageDevice device, NvmeController? c, NvmeHealth? h, IReadOnlyList<NvmeError>? errors, IReadOnlyList<NvmeResponse> responses)
    {
        var health = responses.FirstOrDefault(r => r.Operation == NvmeOperation.Health);
        Metric<T> MakeMetric<T>(T? value, string unit = "") where T : struct => value.HasValue && health?.Succeeded == true
            ? new(value.Value, unit, Availability.Available, $"NVMe SMART/Health log 02h · {health.Scope}", health.ObservedAt) : Metric<T>.Missing(unit);
        string Temp(ushort value) => NvmeBytes.Temperature(value) is double t ? $"{t:F2} °C ({value} K)" : "Unavailable";
        string usage = "Unavailable"; string thermal = "Unavailable";
        if (h is not null)
        {
            string[] names = ["Data Units Read", "Data Units Written", "Host Read Commands", "Host Write Commands", "Controller Busy Time (minutes)", "Power Cycles", "Power On Hours (hours)", "Unsafe Shutdowns", "Media/Data Integrity Errors", "Lifetime Error Information Log Entry Count"];
            usage = string.Join("\n", h.Counters.Select((v, i) => $"{names[i]}: {v}"));
            for (int i = 0; i < 2; i++)
            {
                if (h.Counters[i].IsZero) usage += $"\nHost {(i == 0 ? "read" : "written")} bytes: unavailable (Data Units not reported)";
                else { var bytes = NvmeHealthParser.DataUnitBytes(h.Counters[i]); usage += $"\nHost {(i == 0 ? "read" : "written")} bytes ≈ {bytes} ({(double)bytes / 1e12:F3} TB / {(double)bytes / 1099511627776d:F3} TiB)"; }
            }
            usage += "\nData Units are 1,000 × 512 bytes, rounded up; zero Data Units means not reported. Converted bytes are approximate, exclude metadata and are not current throughput or rated TBW. Other zero counters may be valid.";
            thermal = string.Join("\n", h.Sensors.Select((t, i) => $"Sensor {i + 1}: {(t is double v ? $"{v:F2} °C" : h.RawSensorKelvin.ElementAtOrDefault(i) == 0 ? "Not implemented" : "Unavailable (invalid/sentinel value)")}"));
            thermal += $"\nWarning composite temperature time: {(c?.WarningKelvin is > 0 and < 65535 ? h.WarningMinutes + " minutes" : "Unavailable: valid controller threshold required")}\nCritical composite temperature time: {(c?.CriticalKelvin is > 0 and < 65535 ? h.CriticalMinutes + " minutes" : "Unavailable: valid controller threshold required")}";
            thermal += c?.ThermalManagement == true ? $"\nThermal management T1/T2 transitions: {h.ThermalCounters[0]} / {h.ThermalCounters[1]}\nT1/T2 total time: {h.ThermalCounters[2]} / {h.ThermalCounters[3]} seconds\nA zero thermal-management counter means never occurred or field not implemented." : "\nThermal management counters: unavailable (support/revision not established)";
        }
        static string Normal(string? value) => string.Join(" ", (value ?? "").Trim(' ', '\0').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var differences = c is null ? [] : new[]
        {
            !string.Equals(Normal(device.Model), Normal(c.Model), StringComparison.Ordinal) ? "model" : null,
            !string.Equals(Normal(device.Firmware), Normal(c.Firmware), StringComparison.Ordinal) ? "firmware" : null,
            !string.IsNullOrWhiteSpace(device.Serial) && !string.Equals(Normal(device.Serial), Normal(c.Serial), StringComparison.Ordinal) ? "serial" : null
        }.Where(x => x is not null).ToArray();
        string identity = c is null ? "Identity comparison unavailable because Identify Controller was unavailable." : differences.Length == 0
            ? "Discovery and Identify Controller identity fields agree after trimming defined padding."
            : $"Identity discrepancy remains in: {string.Join(", ", differences)}. Discovery and Identify values are both retained.";
        string controller = c is null ? "Identify Controller unavailable; see per-query outcomes." :
            $"Identify model: {c.Model}\nSerial: redacted (raw payload contains serial)\nFirmware: {c.Firmware}\nVID/SSVID: {c.Vendor:X4}/{c.SubsystemVendor:X4}; Controller ID: {c.Id}\nNVMe version: {(c.Version is 0 or uint.MaxValue ? "Not reported" : $"{c.Version >> 16}.{(c.Version >> 8) & 255}.{c.Version & 255}")}\nNamespace count: {c.Namespaces} (not physical SSD count)\nLPA capabilities: 0x{c.LogAttributes:X2}; Error slots: {c.ErrorSlots}\nWarning / critical threshold: {Temp(c.WarningKelvin)} / {Temp(c.CriticalKelvin)}\n" +
            identity + "\n";
        controller += "\nNamespace ID/details: unavailable; discovery does not establish a namespace mapping. No namespace ID guessed.\nPCIe generation/lane width: unavailable; NVMe version does not establish link speed.";
        var errorResponse = responses.FirstOrDefault(r => r.Operation == NvmeOperation.Errors);
        string errorText = errors is null ? $"Error Information log: {errorResponse?.Outcome}: {errorResponse?.Explanation}" : $"Retrieved {errors.Count} populated entries from 8 bounded slots; unused zero-count slots omitted.\n" +
            string.Join("\n", errors.Select(e => $"Count {e.Count}; SQ {e.Queue}; command {e.Command}; status 0x{e.Status:X4}; location 0x{e.Location:X4}; LBA {e.Lba}; NSID {e.Namespace}"));
        errorText += "\nLifetime log count is separate from entries retrieved and does not alone prove current media failure.";
        string stamp = health is null ? "" : $"\nSource: NVMe SMART/Health 02h · {health.Scope}\nObserved: {health.ObservedAt:O}";
        return new(device.Id, MakeMetric<byte>(h?.Warning), MakeMetric<double>(h?.Celsius, "°C"), MakeMetric<double>(h?.Spare, "%"), MakeMetric<double>(h?.Used, "%"), usage + stamp, thermal + stamp, controller, errorText)
        {
            Controller = c, Health = h, ErrorEntries = errors,
            Responses = responses.ToArray(), WarningDetails = h is null ? $"{health?.Outcome}: {health?.Explanation}" : NvmeHealthParser.Warnings(h.Warning) + stamp,
            SpareThreshold = h?.SpareThreshold is byte threshold ? $"{threshold}%" : "Unavailable",
            IdentitySummary = identity,
            Summary = $"Read finished · {responses.Count(r => r.Succeeded)}/{responses.Count} successful queries; no overall health/QC assessment.",
        };
    }
}

