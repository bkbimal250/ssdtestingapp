using pixinit.Core.Diagnostics.Nvme;
using pixinit.ViewModels.Shared;
using System.Numerics;

namespace pixinit.ViewModels.Nvme;

public sealed partial class NvmeViewModel : DeviceViewModel
{
    private NvmeDiagnostics? result;
    public string CriticalWarning => result?.CriticalWarning.Availability == Core.Diagnostics.Common.Availability.Available
        ? result.CriticalWarning.Value == 0 ? "No critical warning reported" : "Critical warning reported"
        : Availability(result?.CriticalWarning.Availability);
    public string CriticalWarningRaw => result?.CriticalWarning.Availability == Core.Diagnostics.Common.Availability.Available ? $"0x{result.CriticalWarning.Value:X2}" : Availability(result?.CriticalWarning.Availability);
    public string WarningDetails => result?.WarningDetails ?? "Not queried";
    public string SpareThreshold => result?.SpareThreshold ?? "Unavailable";
    public NvmeDiagnostics? Result => result;
    public string Assessment => result?.Assessment is { } a ? $"{a.StateDisplay} · Checklist {a.ChecklistDisplay}" : "Assessment unavailable";
    public string Coverage => result?.Assessment is { } a ? $"Coverage {a.Coverage} for the named NVMe diagnostic checklist; this is not coverage of all device capabilities." : "Checklist coverage unavailable";
    public string ObservedLocal => result?.Assessment is { } a ? a.ObservedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) : "Not observed";
    public string AssessmentDetails => result?.Assessment is { } a ? $"Rule set: {a.RuleSetVersion}\nScope: {a.ProtocolScope}\nObserved UTC: {a.ObservedAt:O}\n{a.Explanation}\n\nMissing evidence:\n{(string.IsNullOrWhiteSpace(a.Missing) ? "None for this checklist" : a.Missing)}\n\nNamespace Identify may remain unavailable without invalidating checks that use independently retrieved SMART/Health evidence.\n\n{Core.Assessment.HealthAssessment.Boundary}" : "No completed assessment.";
    public string RawDetails => result is null ? "Not queried" : string.Join("\n\n", result.Responses.Select(r =>
    {
        byte[] bytes = r.RawResponse.Length == 0 ? r.Payload : r.RawResponse;
        return $"{r.Operation}: {r.Outcome} · {r.ObservedAt:O}\nSource: {r.Source}\nScope: {r.Scope}\n{r.Explanation}\nFixed return: 0x{r.FixedReturn:X8}; parsed payload: {r.Payload.Length} bytes; retained response: {bytes.Length} bytes\n" +
            string.Join("\n", Enumerable.Range(0, (bytes.Length + 15) / 16).Select(i => $"{i * 16:X4}: {Convert.ToHexString(bytes.AsSpan(i * 16, Math.Min(16, bytes.Length - i * 16)))}"));
    }));
    public string Temperature => FormatNumber(result?.Temperature, "F0");
    public string AvailableSpare => FormatNumber(result?.AvailableSpare, "F0");
    public string PercentageUsed => FormatNumber(result?.PercentageUsed, "F0");
    public string Usage => (result?.UsageCounters ?? "Read / write data units, power-on hours and power cycles are unavailable.") + "\nManufacturer-rated TBW: Unavailable (no verified specification profile).\nNAND writes: Unavailable (no verified vendor-specific source).";
    public string Thermal => result?.ThermalDetails ?? "Temperature sensors and thermal management time are unavailable.";
    public string ThermalSummary
    {
        get
        {
            if (result?.Health is not { } h) return "Additional sensors unavailable";
            int available = h.Sensors.Count(x => x is not null), notImplemented = h.RawSensorKelvin.Count(x => x == 0), unavailable = h.Sensors.Count - available - notImplemented;
            return $"{available} reported · {notImplemented} not implemented · {unavailable} unavailable";
        }
    }
    public string Controller => result?.ControllerDetails ?? "NVMe controller and namespace details are not yet queried. Windows-reported firmware is in device information.";
    public string Errors => result?.ErrorDetails ?? "Media errors, unsafe shutdowns and error log entries are unavailable.";
    public string IdentityStatus => result?.IdentitySummary ?? "Identity comparison not queried";
    public string Scope => result?.Responses.FirstOrDefault(r => r.Operation == NvmeOperation.Health)?.Scope ?? "SMART request scope unavailable";
    public string HostReads => FriendlyData(0);
    public string HostWrites => FriendlyData(1);
    public string PowerOnHours => Counter(6, "hours");
    public string PowerCycles => Counter(5, "cycles");
    public string UnsafeShutdowns => Counter(7, "events");
    public string MediaErrors => Counter(8, "errors");
    public sealed record SmartReading(string Field, string Value, string Unit);
    public IReadOnlyList<SmartReading> SmartReadings
    {
        get
        {
            var rows = new List<SmartReading>
            {
                new("Critical warning", CriticalWarning, "Device status"), new("Composite temperature", Temperature, "Celsius"),
                new("Available spare", AvailableSpare, "%"), new("Spare threshold", SpareThreshold, "%"),
                new("Percentage used", PercentageUsed, "Consumed endurance %")
            };
            string[] names = ["Data units read", "Data units written", "Host read commands", "Host write commands", "Controller busy time", "Power cycles", "Power-on time", "Unsafe shutdowns", "Media / data integrity errors", "Error information log entries"];
            string[] units = ["1,000 x 512 bytes (rounded up)", "1,000 x 512 bytes (rounded up)", "commands", "commands", "minutes", "cycles", "hours", "events", "errors", "entries"];
            for (int i = 0; i < names.Length; i++)
                rows.Add(new(names[i], result?.Health is { } h && h.Counters.Count > i ? h.Counters[i].ToString(System.Globalization.CultureInfo.InvariantCulture) : "Unavailable", units[i]));
            return rows;
        }
    }
    public string WarningSummary => result?.Health is not { } h ? "Warnings not queried" : h.Warning == 0 ? "No critical warning reported" : NvmeHealthParser.Warnings(h.Warning);
    public string QuerySummary => result is null ? "Diagnostics not queried" : string.Join(" · ", result.Responses.Select(r => $"{r.Operation}: {Outcome(r.Outcome)}"));
    private static string FormatNumber(Core.Diagnostics.Common.Metric<double>? metric, string format) =>
        metric?.Availability == Core.Diagnostics.Common.Availability.Available && metric.Value is double value
            ? $"{value.ToString(format, System.Globalization.CultureInfo.CurrentCulture)} {metric.Unit}".Trim()
            : metric?.Availability == Core.Diagnostics.Common.Availability.AccessDenied ? "Access denied" : "Unavailable";
    private string Counter(int index, string unit) => result?.Health is { } h && h.Counters.Count > index ? $"{h.Counters[index]:N0} {unit}" : "Unavailable";
    private string FriendlyData(int index)
    {
        if (result?.Health is not { } h || h.Counters.Count <= index || h.Counters[index].IsZero) return "Unavailable";
        BigInteger bytes = NvmeHealthParser.DataUnitBytes(h.Counters[index]); double tebibytes = (double)bytes / 1099511627776d;
        return $"{tebibytes:N2} TiB";
    }
    private static string Availability(Core.Diagnostics.Common.Availability? value) => value switch
    { Core.Diagnostics.Common.Availability.AccessDenied => "Access denied", Core.Diagnostics.Common.Availability.Unsupported => "Unsupported", Core.Diagnostics.Common.Availability.Error => "Error", _ => "Unavailable" };
    private static string Outcome(NvmeOutcome outcome) => outcome switch
    { NvmeOutcome.NotQueried => "Not queried", NvmeOutcome.Unsupported => "Unsupported", NvmeOutcome.AccessDenied => "Access denied", NvmeOutcome.Success => "Available", _ => outcome.ToString() };
    public void Apply(NvmeDiagnostics value) { if (value.DeviceId != Device?.Id) return; result = value; ResultReceived(); NotifyResults(); }
    protected override pixinit.Core.Diagnostics.Common.ThermalInfo? ObservedThermal => result?.Thermal;
    public double? ConsumedPercentage => result?.PercentageUsed.Value;
    protected override void ClearResults() { result = null; NotifyResults(); }

    public string FirstPageWarnings => WarningSummary + "\n" + IdentityStatus;
    public override IReadOnlyList<InfoRow> EntireSsdInfo => IdentityRows(result?.Controller?.Model, result?.Controller?.Firmware, result?.Controller?.Serial).Concat(new InfoRow[]
    {
        new("Power-on hours", FirstPageValue(PowerOnHours)), new("Power cycles", FirstPageValue(PowerCycles)),
        new("Host writes", FirstPageValue(HostWrites), HostWrites == "Unavailable"), new("Host reads", FirstPageValue(HostReads)),
        new("Observed temperature", FirstPageValue(Temperature), result?.Thermal?.ObservedC is null),
        new("Available spare", FirstPageValue(AvailableSpare)), new("Percentage Used (consumed)", FirstPageValue(PercentageUsed)), new("Critical warning", FirstPageValue(CriticalWarning))
    }).ToArray();
    private void NotifyResults()
    {
        NotifyDetailRows();
        foreach (var name in new[] { nameof(FirstPageWarnings), nameof(EntireSsdInfo), nameof(Result), nameof(SmartReadings), nameof(WarningSummary), nameof(Assessment), nameof(Coverage), nameof(ObservedLocal), nameof(AssessmentDetails), nameof(WarningDetails), nameof(SpareThreshold), nameof(RawDetails), nameof(CriticalWarning), nameof(CriticalWarningRaw), nameof(Temperature), nameof(AvailableSpare), nameof(PercentageUsed), nameof(Usage), nameof(Thermal), nameof(ThermalSummary), nameof(Controller), nameof(Errors), nameof(IdentityStatus), nameof(Scope), nameof(HostReads), nameof(HostWrites), nameof(PowerOnHours), nameof(PowerCycles), nameof(UnsafeShutdowns), nameof(MediaErrors), nameof(QuerySummary) }) Changed(name);
    }
}

