using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using pixinit.Core.Assessment;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Core.Diagnostics.Sata;

namespace pixinit.Core.History;

public sealed record SnapshotDevice(string Model, string? Serial, string? Firmware, string Protocol, string Bus,
    uint? NativeBusType, long? CapacityBytes, string IdentityEvidence, string? InstanceId, string? InterfacePath,
    string? MatchKey, bool IdentityReliable);
public sealed record SnapshotQuery(string Name, string Outcome, string Explanation, string Scope, string Source,
    DateTimeOffset ObservedAt, int? NativeError, byte[] Raw);
public sealed record SnapshotMetric(string Key, string? ValueText, string Unit, string Availability, string Source,
    DateTimeOffset? ObservedAt, string Scope);
public sealed record DiagnosticSnapshot(Guid Id, Guid ApplicationSessionId, SnapshotDevice Device, string Protocol,
    string Scope, DateTimeOffset ObservedAtUtc, string CompletionState, IReadOnlyList<SnapshotQuery> Queries,
    IReadOnlyList<SnapshotMetric> Metrics, HealthAssessment Assessment, string ParserVersion, string RuleSetVersion,
    int SchemaVersion = 1)
{
    public const string ReportSchemaVersion = "pixinit.report/1.0";
}

public static class SnapshotFactory
{
    public const string SataParserVersion = "pixinit.sata.parsers/1.0", NvmeParserVersion = "pixinit.nvme.parsers/1.0";
    public static DiagnosticSnapshot Create(Guid appSession, StorageDevice d, SataDiagnostics r)
    {
        var assessment = r.Assessment ?? AssessmentPolicies.Assess(r);
        var queries = r.Commands.Select(c => new SnapshotQuery(c.Operation.ToString(), c.Outcome.ToString(), c.Explanation,
            "Selected SATA device", "Windows ATA buffered pass-through", c.ObservedAt, c.Win32Error, c.Payload)).ToArray();
        var metrics = new List<SnapshotMetric>
        {
            M("sata.smart.return_status", r.SmartPassed.Value?.ToString(), "boolean", r.SmartPassed.Availability.ToString(), r.SmartPassed.Source, r.SmartPassed.ObservedAt, "Selected SATA device")
        };
        foreach (var a in r.Attributes)
        {
            string prefix = $"sata.attribute.{a.Id:X2}.{a.Slot}";
            metrics.Add(M(prefix + ".current", a.Current?.ToString(), "normalized", a.Current is null ? "Unavailable" : "Available", a.InterpretationSource, assessment.ObservedAt, "Selected SATA device"));
            metrics.Add(M(prefix + ".worst", a.Worst?.ToString(), "normalized", a.Worst is null ? "Unavailable" : "Available", a.InterpretationSource, assessment.ObservedAt, "Selected SATA device"));
            metrics.Add(M(prefix + ".threshold", a.Threshold?.ToString(), "normalized", a.Threshold is null ? "Unavailable" : "Available", "Matched by unique attribute ID", assessment.ObservedAt, "Selected SATA device"));
            metrics.Add(M(prefix + ".raw_le48", a.RawValue, "encoding only; no units", "Available", a.InterpretationSource, assessment.ObservedAt, "Selected SATA device"));
        }
        if (r.Tbw is { } tbw)
        {
            metrics.Add(M("sata.host_written_tb", tbw.WrittenTB?.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "TB (decimal host writes)", tbw.WrittenTB is null ? "Unavailable" : tbw.InterpretationVerified ? "Available" : "Unverified interpretation", tbw.Source, assessment.ObservedAt, assessment.ProtocolScope));
            metrics.Add(M("sata.rated_tb_reference", tbw.RatedTB.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "TB", "Unverified reference", "600 TB requested reference; manufacturer rating not verified", assessment.ObservedAt, assessment.ProtocolScope));
        }
        return New(appSession, d, "Sata", "Selected SATA device", queries, metrics, assessment, SataParserVersion);
    }
    public static DiagnosticSnapshot Create(Guid appSession, StorageDevice d, NvmeDiagnostics r)
    {
        var assessment = r.Assessment ?? AssessmentPolicies.Assess(r);
        var queries = r.Responses.Select(q => new SnapshotQuery(q.Operation.ToString(), q.Outcome.ToString(), q.Explanation, q.Scope, q.Source, q.ObservedAt, q.NativeError,
            q.RawResponse.Length == 0 ? q.Payload : q.RawResponse)).ToArray();
        var metrics = new List<SnapshotMetric>
        {
            FromMetric("nvme.critical_warning", r.CriticalWarning, "bitmask", assessment.ProtocolScope),
            FromMetric("nvme.composite_temperature", r.Temperature, "°C", assessment.ProtocolScope),
            FromMetric("nvme.available_spare", r.AvailableSpare, "%", assessment.ProtocolScope),
            FromMetric("nvme.percentage_used", r.PercentageUsed, "% estimated endurance consumed", assessment.ProtocolScope)
        };
        if (r.Health is { } h)
        {
            metrics.Add(M("nvme.available_spare_threshold", h.SpareThreshold?.ToString(), "%", h.SpareThreshold is null ? "Unavailable" : "Available", "NVMe SMART/Health 02h", assessment.ObservedAt, assessment.ProtocolScope));
            string[] names = ["data_units_read", "data_units_written", "host_read_commands", "host_write_commands", "controller_busy_minutes", "power_cycles", "power_on_hours", "unsafe_shutdowns", "media_errors", "error_log_lifetime_count"];
            for (int i = 0; i < names.Length; i++) metrics.Add(M("nvme." + names[i], h.Counters[i].ToString(), i == 4 ? "minutes" : i == 6 ? "hours" : "count", "Available", "NVMe SMART/Health 02h · unsigned 128-bit decimal text", assessment.ObservedAt, assessment.ProtocolScope));
            for (int i = 0; i < h.Sensors.Count; i++) metrics.Add(M($"nvme.temperature_sensor_{i + 1}", h.Sensors[i]?.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "°C", h.Sensors[i] is null ? "Unavailable" : "Available", "NVMe SMART/Health 02h", assessment.ObservedAt, assessment.ProtocolScope));
        }
        if (r.Tbw is { } tbw)
        {
            metrics.Add(M("nvme.host_written_tb", tbw.WrittenTB?.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "TB (decimal host writes)", tbw.WrittenTB is null ? "Unavailable" : tbw.InterpretationVerified ? "Available" : "Unverified interpretation", tbw.Source, assessment.ObservedAt, assessment.ProtocolScope));
            metrics.Add(M("nvme.rated_tb_reference", tbw.RatedTB.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "TB", "Unverified reference", "600 TB requested reference; manufacturer rating not verified", assessment.ObservedAt, assessment.ProtocolScope));
        }
        return New(appSession, d, "Nvme", assessment.ProtocolScope, queries, metrics, assessment, NvmeParserVersion);
    }
    public static DiagnosticSnapshot CreateAttempt(Guid appSession, StorageDevice d, string completionState, string reason)
    {
        if (completionState is not ("Cancelled" or "Failed" or "Partial")) throw new ArgumentOutOfRangeException(nameof(completionState));
        var observed = DateTimeOffset.UtcNow;
        string protocol = d.Protocol == StorageProtocol.Sata ? "Sata" : "Nvme";
        string scope = $"Selected {protocol.ToUpperInvariant()} device";
        string rules = protocol == "Sata" ? AssessmentPolicies.SataVersion : AssessmentPolicies.NvmeVersion;
        string parser = protocol == "Sata" ? SataParserVersion : NvmeParserVersion;
        var check = new AssessmentCheck($"{protocol.ToUpperInvariant()}.SESSION", "Diagnostic session completion", true,
            CheckOutcome.Missing, AssessmentState.Unknown, reason, "PIXINIT session state", observed, scope);
        var assessment = new HealthAssessment(AssessmentState.Unknown, AssessmentCoverage.Insufficient,
            ChecklistResult.Inconclusive, [check], rules, scope, observed);
        return new(Guid.NewGuid(), appSession, DeviceSnapshot(d), protocol, scope, observed, completionState, [], [], assessment, parser, rules);
    }
    private static DiagnosticSnapshot New(Guid session, StorageDevice d, string protocol, string scope, IReadOnlyList<SnapshotQuery> q, IReadOnlyList<SnapshotMetric> m, HealthAssessment a, string parser) =>
        new(Guid.NewGuid(), session, DeviceSnapshot(d), protocol, scope, a.ObservedAt.ToUniversalTime(), a.Coverage == AssessmentCoverage.Complete ? "Completed" : "Partial", q, m, a, parser, a.RuleSetVersion);
    public static SnapshotDevice DeviceSnapshot(StorageDevice d)
    {
        bool reliable = !string.IsNullOrWhiteSpace(d.Serial) && !string.IsNullOrWhiteSpace(d.InstanceId) && !string.IsNullOrWhiteSpace(d.InterfacePath);
        string? key = reliable ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{d.Protocol}|{d.InstanceId}|{d.InterfacePath}|{d.Serial}|{d.Model}|{d.CapacityBytes}"))) : null;
        return new(d.Model, d.Serial, d.Firmware, d.Protocol.ToString(), d.Bus.ToString(), d.NativeBusType, d.CapacityBytes, d.IdentityEvidence, d.InstanceId, d.InterfacePath, key, reliable);
    }
    private static SnapshotMetric FromMetric<T>(string key, Core.Diagnostics.Common.Metric<T> metric, string unit, string scope) where T : struct
    {
        string? text = metric.Value is not T value ? null : value switch
        {
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        return M(key, text, unit, metric.Availability.ToString(), metric.Source, metric.ObservedAt, scope);
    }
    private static SnapshotMetric M(string key, string? value, string unit, string availability, string? source, DateTimeOffset? at, string scope) => new(key, value, unit, availability, source ?? "Unavailable", at, scope);
}

public sealed record MetricComparison(string Key, string Previous, string Current, string? Delta, string Status);
public static class SnapshotComparison
{
    public static IReadOnlyList<MetricComparison> Compare(DiagnosticSnapshot previous, DiagnosticSnapshot current)
    {
        if (!previous.Device.IdentityReliable || !current.Device.IdentityReliable || previous.Device.MatchKey != current.Device.MatchKey || previous.Protocol != current.Protocol || previous.Scope != current.Scope || previous.ObservedAtUtc >= current.ObservedAtUtc) return [];
        var old = previous.Metrics.ToDictionary(m => m.Key);
        var result = new List<MetricComparison>();
        foreach (var now in current.Metrics.Where(m => m.ValueText is not null && m.Availability == "Available"))
        {
            if (!old.TryGetValue(now.Key, out var before) || before.ValueText is null || before.Unit != now.Unit || before.Source != now.Source) continue;
            string? delta = null; string status = "Comparable";
            if (BigInteger.TryParse(before.ValueText, out var a) && BigInteger.TryParse(now.ValueText, out var b))
            { if (b < a) status = "Reset or incompatible decrease"; else delta = (b - a).ToString(); }
            else if (decimal.TryParse(before.ValueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var da) && decimal.TryParse(now.ValueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var db))
            { if (db < da && now.Key.Contains("counter")) status = "Reset or incompatible decrease"; else delta = (db - da).ToString(System.Globalization.CultureInfo.InvariantCulture); }
            result.Add(new(now.Key, before.ValueText, now.ValueText!, delta, status));
        }
        return result;
    }
}
