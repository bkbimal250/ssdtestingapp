using pixinit.Core.Diagnostics.Sata;
using pixinit.ViewModels.Shared;

namespace pixinit.ViewModels.Sata;

public sealed class SataViewModel : DeviceViewModel
{
    private SataDiagnostics? result;
    private SataSmartAttribute? selectedAttribute;
    public string SmartStatus => result?.SmartPassed.Availability == Core.Diagnostics.Common.Availability.Available
        ? (result.SmartPassed.Value == true ? "Threshold not exceeded" : "Threshold exceeded") : result is null ? "Not queried" : result.Commands.FirstOrDefault(c => c.Operation == AtaOperation.SmartStatus)?.Outcome switch
        { AtaOutcome.AccessDenied => "Access denied", AtaOutcome.SmartDisabled => "SMART disabled", AtaOutcome.Unsupported => "Unsupported operation", AtaOutcome.InvalidResponse => "Invalid response", AtaOutcome.Disconnected => "Device disconnected", AtaOutcome.Failed => "Operation failed", _ => "Unknown / not queried" };
    public string SmartExplanation => result?.StatusExplanation ?? "SMART RETURN STATUS is separate from overall health/QC assessment.";
    public string Discrepancies => result?.Discrepancies ?? "";
    public SataDiagnostics? Result => result;
    public string Assessment => result?.Assessment is { } a ? $"{a.StateDisplay} · Checklist {a.ChecklistDisplay}" : "Assessment unavailable";
    public string Coverage => result?.Assessment is { } a ? $"Coverage {a.Coverage} for the named SATA diagnostic checklist; this is not coverage of all device capabilities." : "Checklist coverage unavailable";
    public string ObservedLocal => result?.Assessment is { } a ? a.ObservedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) : "Not observed";
    public string AssessmentDetails => result?.Assessment is { } a ? $"Rule set: {a.RuleSetVersion}\nScope: {a.ProtocolScope}\nObserved UTC: {a.ObservedAt:O}\n{a.Explanation}\n\nMissing evidence:\n{(string.IsNullOrWhiteSpace(a.Missing) ? "None for this checklist" : a.Missing)}\n\n{Core.Assessment.HealthAssessment.Boundary}" : "No completed assessment.";
    public string CommandDetails => result is null ? "Not queried" : string.Join("\n\n", result.Commands.Select(c =>
        $"{c.Operation} · {c.Outcome} · {c.ObservedAt:O}\n{c.Explanation}\nReturned task file: {Convert.ToHexString(c.Registers)}\nPayload ({c.Payload.Length} bytes):\n{Hex(c.Payload)}"));
    private static string Hex(byte[] bytes) => string.Join("\n", Enumerable.Range(0, (bytes.Length + 15) / 16).Select(i => $"{i * 16:X3}: {Convert.ToHexString(bytes.AsSpan(i * 16, Math.Min(16, bytes.Length - i * 16)))}"));
    public string Temperature => Format(result?.Temperature) + (result?.Thermal is { ObservedC: not null, InterpretationVerified: false } ? " (unverified SMART encoding)" : "");
    public string Wear => Format(result?.Wear);
    public IReadOnlyList<SataSmartAttribute> Attributes => result?.Attributes ?? [];
    public SataSmartAttribute? SelectedAttribute { get => selectedAttribute; set { selectedAttribute = value; Changed(); Changed(nameof(AttributeDetail)); } }
    public string AttributeDetail => SelectedAttribute?.Detail ?? "Select a SMART attribute to inspect its source interpretation. Vendor-specific values are not inferred.";
    public string Endurance => result?.EnduranceDetails ?? "Unavailable. Wear and endurance require a verified vendor interpretation.";
    public string Ata => result?.AtaDetails ?? "ATA identity and supported features are unavailable.";
    public string Interface => result?.InterfaceDetails ?? "Negotiated link and interface details are unavailable.";
    public bool HasNoAttributes => Attributes.Count == 0;
    public void Apply(SataDiagnostics value)
    {
        if (value.DeviceId != Device?.Id) return;
        result = value; SelectedAttribute = null; ResultReceived(); NotifyResults();
    }
    protected override pixinit.Core.Diagnostics.Common.ThermalInfo? ObservedThermal => result?.Thermal;
    protected override void ClearResults() { result = null; SelectedAttribute = null; NotifyResults(); }

    public string FirstPageWarnings => SmartStatus + "\n" + Discrepancies;
    public override IReadOnlyList<InfoRow> EntireSsdInfo => IdentityRows(result?.Identity?.Model, result?.Identity?.Firmware, result?.Identity?.Serial).Concat(new InfoRow[]
    {
        new("Power-on hours", result?.PowerOnHours is double hours ? $"{hours:N0} hours (unverified SMART units)" : "N/A"), new("Power cycles", "N/A - no verified interpretation"),
        new("Host writes", result?.Tbw?.WrittenTB is double tb ? $"{tb:N3} TB (unverified SMART units)" : "N/A", result?.Tbw?.WrittenTB is null), new("Host reads", "N/A - no verified interpretation"),
        new("Observed temperature", FirstPageValue(Temperature), result?.Thermal?.ObservedC is null),
        new("Available spare", "N/A - no verified interpretation"), new("Percentage Used (consumed)", "N/A - no verified interpretation"), new("Critical warning", "N/A - NVMe field; SATA SMART status shown below")
    }).ToArray();
    private void NotifyResults()
    {
        foreach (var name in new[] { nameof(FirstPageWarnings), nameof(EntireSsdInfo), nameof(Result), nameof(Assessment), nameof(Coverage), nameof(ObservedLocal), nameof(AssessmentDetails), nameof(SmartStatus), nameof(Temperature), nameof(Wear), nameof(Attributes), nameof(Endurance), nameof(Ata), nameof(Interface), nameof(HasNoAttributes), nameof(SmartExplanation), nameof(Discrepancies), nameof(CommandDetails) }) Changed(name);
    }
}
