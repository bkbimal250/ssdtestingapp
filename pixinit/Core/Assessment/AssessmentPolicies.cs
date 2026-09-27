using pixinit.Core.Diagnostics.Nvme;
using pixinit.Core.Diagnostics.Sata;

namespace pixinit.Core.Assessment;

public static class AssessmentPolicies
{
    public const string SataVersion = "pixinit.sata.health/1.0", NvmeVersion = "pixinit.nvme.health/1.0";

    public static HealthAssessment Assess(SataDiagnostics value)
    {
        var checks = new List<AssessmentCheck>();
        var status = value.Commands.FirstOrDefault(c => c.Operation == AtaOperation.SmartStatus);
        bool? passed = status is null ? null : SmartReturnStatus.Interpret(status);
        checks.Add(C("SATA.SMART_STATUS", "Device-reported SMART status", true,
            passed is null ? CheckOutcome.Missing : passed.Value ? CheckOutcome.Pass : CheckOutcome.Fail,
            passed == false ? AssessmentState.Critical : AssessmentState.NoIssuesDetected,
            passed is null ? status is null ? "SMART RETURN STATUS was not queried." : $"{status.Outcome}: {status.Explanation}" : passed.Value ? "Device-reported threshold condition not exceeded." : "Device-reported threshold condition exceeded.",
            "ATA SMART RETURN STATUS", status?.ObservedAt, "Selected SATA device"));
        foreach (AtaOperation op in new[] { AtaOperation.SmartData, AtaOperation.SmartThresholds })
        {
            var r = value.Commands.FirstOrDefault(c => c.Operation == op); bool valid = r?.Succeeded == true;
            checks.Add(C($"SATA.{op.ToString().ToUpperInvariant()}", op == AtaOperation.SmartData ? "SMART attribute data integrity" : "SMART threshold data integrity", true,
                valid ? CheckOutcome.Pass : CheckOutcome.Missing, AssessmentState.NoIssuesDetected, valid ? "Validated response available." : r is null ? "Not queried." : $"{r.Outcome}: {r.Explanation}", "ATA SMART", r?.ObservedAt, "Selected SATA device"));
        }
        foreach (var a in value.Attributes.Where(a => a.Threshold is not null))
        {
            bool valid = a.Current is >= 1 and <= 253 && a.Threshold is >= 1 and <= 253, failed = valid && a.Current <= a.Threshold;
            checks.Add(C($"SATA.NORMALIZED.{a.Id:X2}.{a.Slot}", $"Normalized attribute {a.Id:X2} threshold", false,
                !valid ? CheckOutcome.Missing : failed ? CheckOutcome.Fail : CheckOutcome.Pass, failed ? AssessmentState.Warning : AssessmentState.NoIssuesDetected,
                !valid ? "Reserved/invalid normalized value; comparison withheld." : $"Current {a.Current}; threshold {a.Threshold}. Raw bytes and vendor meaning are not interpreted.", "Legacy SMART normalized fields", status?.ObservedAt, "Selected SATA device"));
        }
        checks.Add(C("SATA.VENDOR_RAW", "Vendor raw attributes", false, CheckOutcome.NotApplicable, AssessmentState.Unknown,
            "No verified vendor/model profile; raw values are excluded from assessment.", "PIXINIT interpretation boundary", status?.ObservedAt, "Selected SATA device"));
        return Finish(checks, SataVersion, "Selected SATA device");
    }

    public static HealthAssessment Assess(NvmeDiagnostics value)
    {
        var checks = new List<AssessmentCheck>(); var health = value.Health;
        var response = value.Responses.FirstOrDefault(r => r.Operation == NvmeOperation.Health);
        string scope = response?.Scope ?? "NVMe scope unavailable"; DateTimeOffset? at = response?.ObservedAt;
        void Add(string id, string name, bool mandatory, CheckOutcome outcome, AssessmentState severity, string evidence) => checks.Add(C(id, name, mandatory, outcome, severity, evidence, "NVMe SMART/Health log 02h", at, scope));
        if (health is null)
        {
            foreach (var item in new[] { ("CRITICAL_WARNING", "Critical Warning"), ("AVAILABLE_SPARE", "Available Spare threshold"), ("COMPOSITE_TEMPERATURE", "Composite temperature thresholds"), ("MEDIA_ERRORS", "Media/data-integrity errors") })
                Add("NVME." + item.Item1, item.Item2, true, CheckOutcome.Missing, AssessmentState.Unknown, response is null ? "Health log not queried." : $"{response.Outcome}: {response.Explanation}");
        }
        else
        {
            var warningRules = new (int Bit, string Id, string Name, AssessmentState Severity)[]
            { (0,"SPARE_WARNING","Critical Warning: spare below threshold",AssessmentState.Warning), (1,"TEMPERATURE_WARNING","Critical Warning: temperature threshold",AssessmentState.Warning),
              (2,"RELIABILITY_WARNING","Critical Warning: reliability degraded",AssessmentState.Critical), (3,"READ_ONLY_WARNING","Critical Warning: media read-only",AssessmentState.Critical),
              (4,"BACKUP_WARNING","Critical Warning: volatile-memory backup",AssessmentState.Critical) };
            foreach (var w in warningRules) { bool set = (health.Warning & (1 << w.Bit)) != 0; Add("NVME." + w.Id, w.Name, true, set ? CheckOutcome.Fail : CheckOutcome.Pass, set ? w.Severity : AssessmentState.NoIssuesDetected, set ? "Device reports this warning." : "Device does not report this warning."); }
            bool unknown = (health.Warning & 0xE0) != 0;
            Add("NVME.UNKNOWN_WARNING_BITS", "Unknown Critical Warning bits", false, unknown ? CheckOutcome.Attention : CheckOutcome.Pass, unknown ? AssessmentState.Attention : AssessmentState.NoIssuesDetected, $"Unassigned bits: 0x{health.Warning & 0xE0:X2}; no meaning invented.");
            bool spareValid = health.Spare is byte && health.SpareThreshold is byte, spareLow = spareValid && health.Spare!.Value < health.SpareThreshold!.Value;
            Add("NVME.AVAILABLE_SPARE", "Available Spare versus reported threshold", true, !spareValid ? CheckOutcome.Missing : spareLow ? CheckOutcome.Fail : CheckOutcome.Pass, spareLow ? AssessmentState.Warning : AssessmentState.NoIssuesDetected,
                !spareValid ? "Spare or device-reported threshold unavailable." : $"Spare {health.Spare}%; threshold {health.SpareThreshold}%.");
            double? warnC = value.Controller is { WarningKelvin: > 0 and < 65535 } c1 ? c1.WarningKelvin - 273.15 : null;
            double? criticalC = value.Controller is { CriticalKelvin: > 0 and < 65535 } c2 ? c2.CriticalKelvin - 273.15 : null;
            bool tempValid = health.Celsius is double && warnC is double && criticalC is double;
            bool criticalTemp = tempValid && health.Celsius >= criticalC, warningTemp = tempValid && !criticalTemp && health.Celsius >= warnC;
            Add("NVME.COMPOSITE_TEMPERATURE", "Composite temperature versus reported thresholds", true, !tempValid ? CheckOutcome.Missing : criticalTemp || warningTemp ? CheckOutcome.Fail : CheckOutcome.Pass,
                criticalTemp ? AssessmentState.Critical : warningTemp ? AssessmentState.Warning : AssessmentState.NoIssuesDetected,
                !tempValid ? "Temperature or controller-reported thresholds unavailable." : $"{health.Celsius:F2} °C; warning {warnC:F2} °C; critical {criticalC:F2} °C. No application threshold used.");
            var media = health.Counters[8];
            Add("NVME.MEDIA_ERRORS", "Media/data-integrity error counter", true, media > 0 ? CheckOutcome.Attention : CheckOutcome.Pass, media > 0 ? AssessmentState.Attention : AssessmentState.NoIssuesDetected, $"Cumulative counter: {media}. A positive historical count is attention evidence, not by itself a current failure.");
            Add("NVME.PERCENTAGE_USED", "Estimated endurance consumed", false, health.Used >= 100 ? CheckOutcome.Attention : CheckOutcome.Pass, health.Used >= 100 ? AssessmentState.Attention : AssessmentState.NoIssuesDetected, $"Reported Percentage Used: {health.Used}%. No remaining-health or rated-TBW inference.");
            Add("NVME.UNSAFE_SHUTDOWNS", "Unsafe shutdown counter", false, CheckOutcome.NotApplicable, AssessmentState.NoIssuesDetected, $"Cumulative counter: {health.Counters[7]}. Informational only.");
            Add("NVME.ERROR_LOG_COUNT", "Error Information lifetime counter", false, CheckOutcome.NotApplicable, AssessmentState.NoIssuesDetected, $"Cumulative counter: {health.Counters[9]}. Separate from media errors and retrieved entries.");
        }
        return Finish(checks, NvmeVersion, scope);
    }

    private static AssessmentCheck C(string id, string name, bool mandatory, CheckOutcome outcome, AssessmentState severity, string evidence, string source, DateTimeOffset? at, string scope) => new(id, name, mandatory, outcome, severity, evidence, source, at, scope);
    private static HealthAssessment Finish(List<AssessmentCheck> checks, string version, string scope)
    {
        var evaluated = checks.Where(c => c.Outcome is not CheckOutcome.Missing and not CheckOutcome.NotApplicable).ToArray();
        var state = evaluated.Length == 0 ? AssessmentState.Unknown : evaluated.Select(c => c.Severity).OrderByDescending(Rank).First();
        var mandatory = checks.Where(c => c.Mandatory).ToArray(); int valid = mandatory.Count(c => c.Outcome != CheckOutcome.Missing);
        var coverage = valid == mandatory.Length ? AssessmentCoverage.Complete : valid == 0 ? AssessmentCoverage.Insufficient : AssessmentCoverage.Partial;
        bool fail = checks.Any(c => c.Outcome == CheckOutcome.Fail), attention = checks.Any(c => c.Outcome == CheckOutcome.Attention);
        var checklist = fail ? ChecklistResult.Fail : coverage != AssessmentCoverage.Complete ? ChecklistResult.Inconclusive : attention ? ChecklistResult.PassWithAttention : ChecklistResult.Pass;
        return new(state, coverage, checklist, checks, version, scope, checks.Select(c => c.ObservedAt).Where(d => d is not null).Max() ?? DateTimeOffset.UtcNow);
    }
    private static int Rank(AssessmentState s) => s switch { AssessmentState.Critical => 5, AssessmentState.Warning => 4, AssessmentState.Attention => 3, AssessmentState.NoIssuesDetected => 2, _ => 1 };
}
