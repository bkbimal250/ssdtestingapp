using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using pixinit.Core.History;

namespace pixinit.Application.Reporting;

public sealed record ExportOptions(bool RedactSerial = true, bool IncludeRawPayloads = false);
public sealed record DiagnosticReport(string ReportSchemaVersion, DateTimeOffset ExportedAtUtc, DiagnosticSnapshot Snapshot,
    string LargeIntegerRepresentation, bool SerialRedacted, bool RawPayloadsIncluded,
    string Limitation = "Performance, full-surface integrity and future reliability are outside this diagnostic checklist.");

public static class DiagnosticReportExporter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static async Task ExportJsonAsync(DiagnosticSnapshot snapshot, string path, ExportOptions options, CancellationToken token = default)
    {
        var report = Build(snapshot, options); string text = JsonSerializer.Serialize(report, Json);
        await WriteAsync(path, text, token);
    }
    public static async Task ExportTextAsync(DiagnosticSnapshot snapshot, string path, ExportOptions options, CancellationToken token = default)
    {
        var s = Build(snapshot, options).Snapshot; var b = new StringBuilder();
        b.AppendLine($"PIXINIT diagnostic report {DiagnosticSnapshot.ReportSchemaVersion}").AppendLine($"Exported UTC: {DateTimeOffset.UtcNow:O}")
            .AppendLine($"Observed UTC: {s.ObservedAtUtc:O}").AppendLine($"Session: {s.Id}").AppendLine($"Device: {s.Device.Model}")
            .AppendLine($"Serial: {s.Device.Serial ?? "Unavailable"}").AppendLine($"Firmware: {s.Device.Firmware ?? "Unavailable"}")
            .AppendLine($"Protocol / scope: {s.Protocol} / {s.Scope}").AppendLine($"Identity reliable for comparison: {s.Device.IdentityReliable}")
            .AppendLine($"Completion: {s.CompletionState}").AppendLine($"Assessment: {s.Assessment.StateDisplay}")
            .AppendLine($"Coverage: {s.Assessment.Coverage}").AppendLine($"Checklist: {s.Assessment.ChecklistDisplay}")
            .AppendLine($"Parser / rules / schema: {s.ParserVersion} / {s.RuleSetVersion} / {s.SchemaVersion}")
            .AppendLine(Health.AssessmentBoundary()).AppendLine().AppendLine("CHECKS");
        foreach (var c in s.Assessment.Checks) b.AppendLine($"{c.RuleId} | {c.Outcome} | {c.Name} | {c.Evidence} | {c.Source} | {c.Scope} | {c.ObservedAt:O}");
        b.AppendLine().AppendLine("QUERIES"); foreach (var q in s.Queries) b.AppendLine($"{q.Name} | {q.Outcome} | {q.Scope} | {q.Source} | {q.ObservedAt:O} | {q.Explanation} | rawBytes={q.Raw.Length}");
        b.AppendLine().AppendLine("METRICS (large counters are lossless decimal text)"); foreach (var m in s.Metrics) b.AppendLine($"{m.Key} | {m.ValueText ?? "Unavailable"} | {m.Unit} | {m.Availability} | {m.Source} | {m.Scope} | {m.ObservedAt:O}");
        b.AppendLine().AppendLine("Missing information:").AppendLine(string.IsNullOrWhiteSpace(s.Assessment.Missing) ? "None for mandatory checklist inputs." : s.Assessment.Missing);
        await WriteAsync(path, b.ToString(), token);
    }
    private static DiagnosticReport Build(DiagnosticSnapshot source, ExportOptions options)
    {
        var device = source.Device with { Serial = options.RedactSerial && source.Device.Serial is not null ? "REDACTED" : source.Device.Serial };
        var queries = source.Queries.Select(q => options.IncludeRawPayloads ? q : q with { Raw = [] }).ToArray();
        return new(DiagnosticSnapshot.ReportSchemaVersion, DateTimeOffset.UtcNow, source with { Device = device, Queries = queries },
            "Metric values, including UInt128/BigInteger counters, are decimal strings in Snapshot.Metrics[].ValueText.", options.RedactSerial, options.IncludeRawPayloads);
    }
    private static async Task WriteAsync(string path, string text, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.GetExtension(path).Length == 0) throw new ArgumentException("A destination file with an extension is required.", nameof(path));
        string full = Path.GetFullPath(path); string? directory = Path.GetDirectoryName(full); if (directory is null || !Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        string temp = full + ".pixinit.tmp"; try { await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), token); File.Move(temp, full, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static class Health { internal static string AssessmentBoundary() => Core.Assessment.HealthAssessment.Boundary; }
}
