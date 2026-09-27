using pixinit.Core.Diagnostics.Nvme;
using pixinit.ViewModels.Shared;

namespace pixinit.ViewModels.Nvme;

public sealed class NvmeViewModel : DeviceViewModel
{
    private NvmeDiagnostics? result;
    public string CriticalWarning => result?.CriticalWarning.Availability == Core.Diagnostics.Common.Availability.Available ? $"0x{result.CriticalWarning.Value:X2}" : "Unavailable";
    public string WarningDetails => result?.WarningDetails ?? "Not queried";
    public string SpareThreshold => result?.SpareThreshold ?? "Unavailable";
    public NvmeDiagnostics? Result => result;
    public string Assessment => result?.Assessment is { } a ? $"{a.StateDisplay} · Coverage {a.Coverage} · Checklist {a.ChecklistDisplay}" : "Assessment unavailable";
    public string AssessmentDetails => result?.Assessment is { } a ? $"Rule set: {a.RuleSetVersion}\nScope: {a.ProtocolScope}\nObserved: {a.ObservedAt:O}\n{a.Explanation}\n\nMissing evidence:\n{(string.IsNullOrWhiteSpace(a.Missing) ? "None" : a.Missing)}\n\n{Core.Assessment.HealthAssessment.Boundary}" : "No completed assessment.";
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
    public string Controller => result?.ControllerDetails ?? "NVMe controller and namespace details are not yet queried. Windows-reported firmware is in device information.";
    public string Errors => result?.ErrorDetails ?? "Media errors, unsafe shutdowns and error log entries are unavailable.";
    private static string FormatNumber(Core.Diagnostics.Common.Metric<double>? metric, string format) =>
        metric?.Availability == Core.Diagnostics.Common.Availability.Available && metric.Value is double value
            ? $"{value.ToString(format, System.Globalization.CultureInfo.CurrentCulture)} {metric.Unit}".Trim()
            : metric?.Availability == Core.Diagnostics.Common.Availability.AccessDenied ? "Access denied" : "Unavailable";
    public void Apply(NvmeDiagnostics value) { if (value.DeviceId != Device?.Id) return; result = value; ResultReceived(); NotifyResults(); }
    protected override void ClearResults() { result = null; NotifyResults(); }
    private void NotifyResults()
    {
        foreach (var name in new[] { nameof(Result), nameof(Assessment), nameof(AssessmentDetails), nameof(WarningDetails), nameof(SpareThreshold), nameof(RawDetails), nameof(CriticalWarning), nameof(Temperature), nameof(AvailableSpare), nameof(PercentageUsed), nameof(Usage), nameof(Thermal), nameof(Controller), nameof(Errors) }) Changed(name);
    }
}

