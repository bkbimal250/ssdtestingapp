using System.Globalization;
using pixinit.Core.Diagnostics.Nvme;

namespace pixinit.ViewModels.Nvme;

public sealed partial class NvmeViewModel
{
    public sealed record DetailRow(string Label, string Value, string Color = "#64748B");
    private DetailRow Request(NvmeOperation operation)
    {
        var response = result?.Responses.FirstOrDefault(r => r.Operation == operation);
        string state = response is null ? "Not queried" : Outcome(response.Outcome);
        return new(operation.ToString(), state, response?.Succeeded == true ? "#15803D" : response is null || response.Outcome == NvmeOutcome.NotQueried ? "#A16207" : "#64748B");
    }
    public IReadOnlyList<DetailRow> RequestRows => Enum.GetValues<NvmeOperation>().Select(Request).ToArray();
    public string CriticalWarningColor => result?.CriticalWarning.Availability == Core.Diagnostics.Common.Availability.Available ? result.CriticalWarning.Value == 0 ? "#15803D" : "#B91C1C" : "#64748B";
    public IReadOnlyList<SmartReading> ExactCounterRows => SmartReadings.Skip(5).Take(8).Select(r => r with { Value = r.Value == "Unavailable" ? "N/A - SMART/Health counter not observed" : r.Value }).ToArray();
    public string ReportedSensors => $"Reported {SensorRows.Count(r => r.Color == "#15803D")}";
    public string NotImplementedSensors => $"Not Implemented {SensorRows.Count(r => r.Value.Contains("Not implemented", StringComparison.Ordinal))}";
    public string UnavailableSensors => $"Unavailable {SensorRows.Count(r => r.Value.Contains("not observed", StringComparison.Ordinal))}";
    public IReadOnlyList<DetailRow> SensorRows => Enumerable.Range(0, 8).Select(i =>
    {
        var h = result?.Health;
        if (h is not null && h.Sensors.Count > i && h.Sensors[i] is double c) return new DetailRow($"Sensor {i + 1}", $"{c:F2} C", "#15803D");
        if (h is not null && h.RawSensorKelvin.Count > i && h.RawSensorKelvin[i] == 0) return new DetailRow($"Sensor {i + 1}", "N/A - Not implemented by controller");
        return new DetailRow($"Sensor {i + 1}", "N/A - Sensor field not observed");
    }).ToArray();
    public string CompositeDetail => result?.Health?.Celsius is double c ? $"{c:F2} C; {Core.Diagnostics.Common.ThermalInfo.Classify(c)}; observed {result.Responses.FirstOrDefault(r => r.Operation == NvmeOperation.Health)?.ObservedAt.ToLocalTime():g}" : "N/A - Composite temperature not observed";
    private static string Threshold(ushort? kelvin) => kelvin is > 0 ? $"{kelvin.Value - 273.15:F2} C ({kelvin} K)" : "N/A - Controller threshold not reported";
    public IReadOnlyList<DetailRow> ThermalRows => [new("Composite temperature (SMART/Health)", CompositeDetail), new("Warning threshold (Identify Controller)", Threshold(result?.Controller?.WarningKelvin)), new("Critical threshold (Identify Controller)", Threshold(result?.Controller?.CriticalKelvin))];
    public IReadOnlyList<DetailRow> ControllerRows
    {
        get
        {
            var c = result?.Controller;
            const string missing = "N/A - Identify Controller not available";
            return [new("Identify model", c?.Model ?? missing), new("Identify serial", c?.Serial ?? missing), new("Firmware", c?.Firmware ?? missing), new("VID / SSVID; Controller ID", c is null ? missing : $"{c.Vendor:X4} / {c.SubsystemVendor:X4}; {c.Id}"), new("NVMe version", c is null ? missing : $"{c.Version >> 16}.{(c.Version >> 8) & 255}.{c.Version & 255}"), new("Namespace count (not physical SSD count)", c?.Namespaces.ToString(CultureInfo.InvariantCulture) ?? missing), new("LPA; error log slots", c is null ? missing : $"0x{c.LogAttributes:X2}; {c.ErrorSlots}"), new("Warning / critical threshold", $"{Threshold(c?.WarningKelvin)} / {Threshold(c?.CriticalKelvin)}")];
        }
    }
    public string NamespaceDisclosure => result?.Responses.FirstOrDefault(r => r.Operation == NvmeOperation.Namespace) is { } r ? $"Namespace: {Outcome(r.Outcome)}. {r.Explanation}. Scope: {r.Scope}" : "Namespace ID/details: N/A - discovery does not establish a namespace mapping. No namespace ID guessed.";
    public IReadOnlyList<DetailRow> ErrorRows => [new("Media / data integrity errors", ExactError(8)), new("Number of error information log entries", ExactError(9)), new("Retrieved error log entries", result?.ErrorEntries is { } entries ? entries.Count.ToString(CultureInfo.InvariantCulture) : "N/A - Error log not retrieved")];
    private string ExactError(int i) => result?.Health is { } h && h.Counters.Count > i ? h.Counters[i].ToString(CultureInfo.InvariantCulture) : "N/A - SMART/Health counter not observed";
    private void NotifyDetailRows()
    {
        foreach (string name in new[] { nameof(RequestRows), nameof(CriticalWarningColor), nameof(ExactCounterRows), nameof(SensorRows), nameof(ReportedSensors), nameof(NotImplementedSensors), nameof(UnavailableSensors), nameof(ThermalRows), nameof(ControllerRows), nameof(NamespaceDisclosure), nameof(ErrorRows) }) Changed(name);
    }
}