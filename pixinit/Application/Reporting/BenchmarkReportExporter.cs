using System.Text;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using pixinit.Core.Benchmark;

namespace pixinit.Application.Reporting;

public sealed record BenchmarkReport(string ReportSchemaVersion, DateTimeOffset ExportedAtUtc, BenchmarkSession Session,
    IReadOnlyDictionary<string, string> LosslessIntegerValues, bool SerialRedacted,
    string Units = "MB/s = bytes/second / 1,000,000; MiB/s = bytes/second / 1,048,576; IOPS = completed operations / measured seconds.",
    string Limitations = "Buffered filesystem benchmark — QD1; caching affects results. No uncached device speed, durable-write latency, sustained post-cache performance, or physical NAND writes are claimed. Sustained-write testing is not implemented. Filesystem, encryption, controller/SLC cache, thermal and power state, background activity, free space, firmware, test size and workload affect results. One run is not permanent capability.");

public static class BenchmarkReportExporter
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static async Task ExportJsonAsync(BenchmarkSession session, string path, bool redactSerial = true, CancellationToken token = default) => await Write(path, JsonSerializer.Serialize(Build(session, redactSerial), Json), token);
    public static async Task ExportTextAsync(BenchmarkSession source, string path, bool redactSerial = true, CancellationToken token = default)
    {
        var report = Build(source, redactSerial); var s = report.Session; var b = new StringBuilder();
        b.AppendLine($"PIXINIT benchmark report {report.ReportSchemaVersion}").AppendLine("Buffered filesystem benchmark — QD1; caching affects results.").AppendLine($"Exported UTC: {report.ExportedAtUtc:O}").AppendLine($"Observed: {s.StartedUtc:O} – {s.FinishedUtc:O}")
            .AppendLine($"Policy / app: {s.PolicyVersion} / {s.ApplicationVersion}").AppendLine($"Target: {s.Target.Directory} ({s.Target.FileSystem}; {s.Target.VolumeRoot})")
            .AppendLine($"Device: {s.Target.Device?.Model ?? "Uncertain"}; serial: {s.Target.Device?.Serial ?? "Unavailable"}").AppendLine($"Identity: {s.Target.MappingEvidence}")
            .AppendLine($"Preset / operations: {s.Configuration.Preset} / {s.Configuration.Operations}").AppendLine($"File / blocks: {s.Configuration.FileSizeBytes} / seq {s.Configuration.SequentialBlockBytes} / random {s.Configuration.RandomBlockBytes} bytes")
            .AppendLine($"Iterations / random operations / queue: {s.Configuration.Iterations} / {s.Configuration.RandomOperationCount} / {s.Configuration.QueueDepth}").AppendLine($"I/O: {s.Configuration.IoMode}")
            .AppendLine($"Maximum/application data bytes are not physical NAND writes: {s.Configuration.MaximumWriteBytes()}").AppendLine($"Consent required/granted: {s.WriteConsentRequired}/{s.WriteConsentGranted}").AppendLine($"Completion: {s.Completion}; reason: {s.Reason ?? "None"}")
            .AppendLine($"Temperature before: {Temperature(s.BeforeTemperature)}").AppendLine($"Temperature after: {Temperature(s.AfterTemperature)}")
            .AppendLine($"Preparation: {s.PreparationBytesWritten} bytes, {s.PreparationSeconds:R} s; warm-up {s.WarmupSeconds:R} s; cleanup {s.CleanupSeconds:R} s; total {s.TotalSeconds:R} s; cleanup succeeded {s.CleanupSucceeded}").AppendLine();
        foreach (var r in s.Results) b.AppendLine($"{r.Operation} | {r.State} | bytes={r.BytesProcessed} | operations={r.CompletedOperations} | measured={r.MeasuredSeconds:R}s | {r.MegabytesPerSecond:R} MB/s | {r.MebibytesPerSecond:R} MiB/s | IOPS={r.Iops?.ToString("R") ?? "N/A"} | latency avg/min/max ms={r.Latency?.AverageMilliseconds:R}/{r.Latency?.MinimumMilliseconds:R}/{r.Latency?.MaximumMilliseconds:R}");
        b.AppendLine().AppendLine(report.Units).AppendLine(s.Methodology).AppendLine(report.Limitations);
        await Write(path, b.ToString(), token);
    }
    private static BenchmarkReport Build(BenchmarkSession source, bool redact)
    {
        var device = source.Target.Device is null ? null : source.Target.Device with { Serial = redact && source.Target.Device.Serial is not null ? "REDACTED" : source.Target.Device.Serial };
        var s = source with { Target = source.Target with { Device = device } };
        var integers = new Dictionary<string, string> { ["fileSizeBytes"] = s.Configuration.FileSizeBytes.ToString(), ["preparationBytesWritten"] = s.PreparationBytesWritten.ToString() };
        for (int i = 0; i < s.Results.Count; i++) { integers[$"results[{i}].bytesProcessed"] = s.Results[i].BytesProcessed.ToString(); integers[$"results[{i}].completedOperations"] = s.Results[i].CompletedOperations.ToString(); }
        return new(BenchmarkSession.ReportSchemaVersion, DateTimeOffset.UtcNow, s, integers, redact);
    }
    private static string Temperature(BenchmarkTemperature? value) => value is null ? "Unavailable" : $"{value.Celsius:R} C; source={value.Source}; scope={value.Scope}; observed={value.ObservedAt:O}";
    private static async Task Write(string path, string text, CancellationToken token)
    {
        string full = Path.GetFullPath(path); string? directory = Path.GetDirectoryName(full); if (string.IsNullOrWhiteSpace(Path.GetExtension(full)) || directory is null || !Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        string temp = full + ".pixinit.tmp"; try { await File.WriteAllTextAsync(temp, text, new UTF8Encoding(false), token); File.Move(temp, full, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
