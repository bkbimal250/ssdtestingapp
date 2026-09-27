namespace pixinit.Core.Assessment;

public enum AssessmentState { NoIssuesDetected, Attention, Warning, Critical, Unknown }
public enum AssessmentCoverage { Complete, Partial, Insufficient }
public enum ChecklistResult { Pass, PassWithAttention, Fail, Inconclusive }
public enum CheckOutcome { Pass, Attention, Fail, Missing, NotApplicable }

public sealed record AssessmentCheck(string RuleId, string Name, bool Mandatory, CheckOutcome Outcome,
    AssessmentState Severity, string Evidence, string Source, DateTimeOffset? ObservedAt, string Scope);

public sealed record HealthAssessment(AssessmentState State, AssessmentCoverage Coverage, ChecklistResult Checklist,
    IReadOnlyList<AssessmentCheck> Checks, string RuleSetVersion, string ProtocolScope, DateTimeOffset ObservedAt)
{
    public string StateDisplay => State switch { AssessmentState.NoIssuesDetected => "No Issues Detected", _ => State.ToString() };
    public string ChecklistDisplay => Checklist switch { ChecklistResult.PassWithAttention => "PASS WITH ATTENTION", ChecklistResult.Inconclusive => "INCONCLUSIVE", ChecklistResult.Fail => "FAIL", _ => "PASS" };
    public string Explanation => string.Join("\n", Checks.Select(c => $"{c.Name}: {c.Outcome} — {c.Evidence} [{c.RuleId}]"));
    public string Missing => string.Join("\n", Checks.Where(c => c.Outcome == CheckOutcome.Missing).Select(c => $"{c.Name}: {c.Evidence}"));
    public const string Boundary = "Checklist excludes performance, full-surface integrity and future reliability.";
}
