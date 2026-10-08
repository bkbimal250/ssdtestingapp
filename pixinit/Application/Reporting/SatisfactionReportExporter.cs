using System.Globalization;
using System.Text;
using System.Text.Json;
using pixinit.Core.Benchmark;

namespace pixinit.Application.Reporting;

public sealed record CompanyMetric(string Label, double? Value, string Unit, string? Reason, string Source, DateTimeOffset? ObservedAtUtc);
public sealed record CompanyCriterion(string Name, string Result, string Explanation);
public sealed record SatisfactionReport(string Schema, DateTimeOffset ExportedAtUtc, DateTimeOffset ObservedUtc, string Policy,
    string Completion, string? Model, string? Serial, string Target, string FileSystem, string Mapping,
    IReadOnlyList<CompanyMetric> Metrics, IReadOnlyList<CompanyCriterion> Criteria, string Result, string Scope);

public static partial class BenchmarkReportExporter
{
    public static SatisfactionReport BuildSatisfactionReport(BenchmarkSession s, bool redact = true)
    {
        BenchmarkOperationResult? Op(BenchmarkOperation o) => s.Results.FirstOrDefault(r => r.Operation == o && r.State == BenchmarkOperationState.Completed);
        var read = Op(BenchmarkOperation.SequentialRead); var write = Op(BenchmarkOperation.SequentialWrite);
        var temp = s.Thermal is { InterpretationVerified: true } ? s.Thermal.ObservedC : null;
        var used = s.PercentageUsed;
        if (temp is double t && !double.IsFinite(t)) temp = null;
        if (used is double p && (!double.IsFinite(p) || p < 0)) used = null;
        var metrics = new List<CompanyMetric>();
        void Add(string label, double? value, string unit, string reason, string source, DateTimeOffset? observed = null)
        { bool valid = value is double v && double.IsFinite(v); metrics.Add(new(label, valid ? value : null, unit, valid ? null : reason, source, observed)); }
        Add("SEQ Read", read?.MegabytesPerSecond, "MB/s", "No completed sequential read", "Buffered filesystem QD1");
        Add("SEQ Write", write?.MegabytesPerSecond, "MB/s", "No completed sequential write", "Buffered filesystem QD1; FlushAsync to OS");
        Add("RND Read", s.RndReadIOPS, "IOPS", "No completed 4 KiB random read", "1000 / mean latency ms");
        Add("RND Write", s.RndWriteIOPS, "IOPS", "No completed 4 KiB random write", "1000 / mean latency ms");
        Add("Sustained overall", s.SustainedOverallMBs, "MB/s", "Sustained not run or no timed measurement", "Actual total bytes / actual total seconds; completion status applies");
        Add("Sustained post-drop", s.SustainedPostDropMBs, "MB/s", "No qualifying drop or incomplete workload", "Drop candidate; not proven SLC exhaustion");
        Add("Host writes", s.Tbw?.WrittenTB, "TB", "No mapped diagnostic host-write reading", s.Tbw?.Source ?? "Not observed");
        Add("Current temperature", s.Thermal?.ObservedC, "C", "No mapped diagnostic temperature reading", s.Thermal?.Source ?? "Not observed", s.Thermal?.ObservedAt);
        Add("Idle temperature", s.Thermal?.IdleC, "C", "Capture Idle required", s.Thermal?.IdleCondition ?? "Not captured", s.Thermal?.IdleTempObservedAt);
        Add("Load temperature", s.Thermal?.LoadC, "C", "Capture Load required or post-sustained read unavailable", s.Thermal?.LoadCondition ?? "Not captured", s.Thermal?.LoadTempObservedAt);
        Add("Percentage Used", used, "% consumed endurance", "No available NVMe consumed-endurance reading", "Diagnostic snapshot");
        var criteria = new[]
        {
            new CompanyCriterion("Temperature < 80 C", temp is null ? "N/A" : temp < 80 ? "PASS" : "FAIL", temp is null ? "Verified temperature not observed" : $"{temp:F1} C observed snapshot"),
            new CompanyCriterion("Percentage Used < 90%", used is null ? "N/A" : used < 90 ? "PASS" : "FAIL", used is null ? "Consumed-endurance field unavailable" : $"{used:F1}% consumed"),
            new CompanyCriterion("SEQ Read > 500 MB/s", read is null ? "N/A" : read.MegabytesPerSecond > 500 ? "PASS" : "FAIL", "User-defined buffered QD1 criterion; not a manufacturer capability guarantee")
        };
        string verdict = criteria.Any(c => c.Result == "FAIL") ? "FAIL" : s.Completion != BenchmarkCompletion.Completed || criteria.Any(c => c.Result == "N/A") ? "INCOMPLETE" : "PASS";
        return new(BenchmarkSession.ReportSchemaVersion, DateTimeOffset.UtcNow, s.FinishedUtc, s.PolicyVersion, s.Completion.ToString(), s.Target.Device?.Model,
            redact && s.Target.Device?.Serial is not null ? "REDACTED" : s.Target.Device?.Serial, s.Target.Directory, s.Target.FileSystem, s.Target.MappingEvidence, metrics, criteria, verdict,
            "Company acceptance criteria only, separate from evidence-based device health. Buffered filesystem QD1; caching affects results. No uncached/durable NAND speed or proven SLC exhaustion. 600 TB is an unverified reference, not a rating. Missing fields do not imply zero or PASS. Idle/load capture conditions and completion status apply.");
    }
    public static async Task ExportSatisfactionReport(BenchmarkSession session, string path, bool json = false, bool redactSerial = true, CancellationToken token = default)
    {
        var report = BuildSatisfactionReport(session, redactSerial);
        if (json) { await Write(path, JsonSerializer.Serialize(report, Json), token); return; }
        var text = new StringBuilder().AppendLine("PIXINIT company acceptance report " + report.Schema)
            .AppendLine($"Result: {report.Result}; benchmark: {report.Completion}; policy: {report.Policy}")
            .AppendLine($"Observed UTC: {report.ObservedUtc:O}; exported UTC: {report.ExportedAtUtc:O}")
            .AppendLine($"Device: {report.Model ?? "N/A - identity not established"}; serial: {report.Serial ?? "N/A - not reported"}")
            .AppendLine($"Target: {report.Target}; filesystem: {report.FileSystem}").AppendLine(report.Mapping);
        foreach (var metric in report.Metrics) text.AppendLine($"{metric.Label}: {(metric.Value is double value ? value.ToString("F2", CultureInfo.InvariantCulture) + " " + metric.Unit : "N/A - " + metric.Reason)}; source: {metric.Source}; observed UTC: {metric.ObservedAtUtc:O}");
        foreach (var criterion in report.Criteria) text.AppendLine($"{criterion.Name}: {criterion.Result}; {criterion.Explanation}");
        text.AppendLine(report.Scope); await Write(path, text.ToString(), token);
    }
}
