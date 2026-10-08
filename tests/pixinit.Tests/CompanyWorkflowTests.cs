using System.IO;
using System.Text.Json;
using pixinit.Application.Benchmarking;
using pixinit.Application.Reporting;
using pixinit.Application.Scanning;
using pixinit.Core.Benchmark;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;
using pixinit.Infrastructure.Benchmarking;
using pixinit.ViewModels.Benchmark;
using pixinit.ViewModels.Shared;

namespace pixinit.Tests;
internal static partial class Program
{
    private sealed class CaptureDevice : DeviceViewModel
    {
        public ThermalInfo? Reading { get; set; }
        protected override ThermalInfo? ObservedThermal => Reading;
        protected override void ClearResults() { Reading = null; }
    }
    private static async Task CompanyWorkflowTests()
    {
        var mixed = BenchmarkPolicy.Quick with { FileSizeBytes = 1024 * BenchmarkPolicy.MiB, Operations = BenchmarkPolicy.Quick.Operations | BenchmarkOperation.SustainedWrite, SustainedDurationSeconds = 30 };
        BenchmarkPolicy.Validate(mixed, 5_000_000_000);
        Check(BenchmarkPolicy.Version == "benchmark-policy-v3" && mixed.RequiredFileBytes == 1_073_741_824 && mixed.SustainedMaximumWriteBytes == 34_359_738_368, "1024 MiB ordinary plus 30-second sustained validates at five billion free bytes with separate footprints/caps");
        var capture = new CaptureDevice(); var disk = Drive("capture", StorageProtocol.Nvme);
        capture.SetDevice(disk); int reads = 0;
        capture.ConfigureThermalCapture(_ => { reads++; capture.Reading = new(null, null, ThermalState.Green, 39.9, "Injected", DateTimeOffset.UtcNow); return Task.CompletedTask; }, _ => Task.FromResult(true), () => true, () => { });
        capture.MarkBenchmarkFinished(DateTimeOffset.UtcNow);
        await capture.CaptureTemperatureAsync(true);
        Check(capture.IdleTemp is null && reads == 0 && capture.ThermalCaptureStatus.Contains("Wait 10 min idle"), "Recent benchmark blocks idle capture without falsely labeling current temperature idle");
        capture.MarkBenchmarkFinished(DateTimeOffset.UtcNow.AddMinutes(-11)); await capture.CaptureTemperatureAsync(true);
        Check(capture.IdleTemp == 39.9 && capture.IdleTempObservedAt is not null && capture.ThermalSnapshot!.IdleCondition.Contains("Ten-minute"), "Verified idle window stores temperature, timestamp and condition independently");
        await capture.CaptureTemperatureAsync(false);
        Check(capture.LoadTemp == 39.9 && capture.LoadTempObservedAt is not null && capture.ThermalSnapshot!.LoadCondition.Contains("not established"), "Manual load capture records its unestablished workload condition");
        capture.ConfigureThermalCapture(_ => { capture.Reading = new(null, null, ThermalState.Green, 45, "Injected", DateTimeOffset.UtcNow.AddMinutes(-1)); return Task.CompletedTask; }, _ => Task.FromResult(false), () => true, () => { });
        await capture.CaptureTemperatureAsync(true);
        Check(capture.IdleTemp == 39.9 && capture.ThermalCaptureStatus.Contains("cannot be verified"), "Unverified or active disk cannot overwrite verified idle observation");
        await capture.CaptureTemperatureAsync(false);
        Check(capture.LoadTemp == 39.9 && capture.ThermalCaptureStatus.Contains("fresh reading"), "Stale diagnostic temperature cannot become a new load capture");
        capture.SetDevice(Drive("different", StorageProtocol.Nvme));
        Check(capture.IdleTemp is null && capture.LoadTemp is null, "Switching devices clears captured thermal observations");
        capture.ConfigureThermalCapture(_ => Task.CompletedTask, token => Task.Delay(Timeout.Infinite, token).ContinueWith(_ => true, token), () => true, () => { });
        var waiting = capture.CaptureTemperatureAsync(true); capture.CancelThermalCapture(); await waiting;
        Check(!capture.ThermalCaptureBusy && capture.ThermalCaptureStatus.Contains("cancelled"), "Idle verification cancellation releases UI state without saving an observation");
        string root = Path.Combine(Path.GetTempPath(), "pixinit-company-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var selected = disk with { MountPoints = [Path.GetPathRoot(root)!], InterfacePath = "test", InstanceId = "test" };
            var gate = new StorageOperationGate(); var engine = new FileBenchmarkEngine(); var config = BenchmarkPolicy.Quick with { Operations = BenchmarkOperation.SustainedWrite, SustainedMaximumWriteBytes = BenchmarkPolicy.MiB, SustainedDurationSeconds = 1 };
            ThermalInfo? thermal = new(null, null, ThermalState.Green, 35, "Injected", DateTimeOffset.UtcNow); int loadCalls = 0;
            var vm = new BenchmarkViewModel(engine, null, () => [selected], () => selected, () => { }, new Consent(true), operationGate: gate, thermal: () => thermal,
                afterSustained: _ => { using var lease = gate.Enter(); loadCalls++; thermal = thermal! with { ObservedC = 40, LoadC = 40, LoadTempObservedAt = DateTimeOffset.UtcNow, ObservedAt = DateTimeOffset.UtcNow, LoadCondition = "Post-sustained observation" }; return Task.CompletedTask; }, percentageUsed: () => 20)
            { TargetDirectory = root, SequentialRead = false, SequentialWrite = false, RandomRead = false, RandomWrite = false, SustainedWrite = true, SustainedBudgetMiB = 1, SustainedDurationSeconds = 1, WaitForLoadCapture = _ => Task.CompletedTask };
            await vm.RunAsync(); var session = vm.Result!;
            Check(loadCalls == 1 && session.Thermal?.LoadC == 40 && session.PercentageUsed == 20 && session.CleanupSucceeded, "Post-sustained diagnostic capture runs after lease release and persists load context on bounded one MiB run");
            Check(session.SafetyPolicy is not null && BenchmarkComparisons.Compare(session with { SafetyPolicy = null }, session).Count == 0, "Explicit safety policy prevents comparisons with older sessions reusing policy v3");
            var fastRead = BenchmarkOperationResult.Calculate(BenchmarkOperation.SequentialRead, 600_000_000, 1, System.Diagnostics.Stopwatch.Frequency, null);
            var completed = session with { Results = [fastRead], Thermal = thermal, PercentageUsed = 20 };
            var report = BenchmarkReportExporter.BuildSatisfactionReport(completed);
            Check(report.Result == "PASS" && report.Criteria.All(c => c.Result == "PASS") && report.Serial == "REDACTED", "Company report passes only the three specified acceptance criteria and redacts serial");
            Check(BenchmarkReportExporter.BuildSatisfactionReport(completed with { PercentageUsed = null }).Result == "INCOMPLETE" && BenchmarkReportExporter.BuildSatisfactionReport(completed with { Thermal = thermal! with { ObservedC = 80 } }).Result == "FAIL", "Missing criteria remain incomplete and exact 80 C fails company criterion independently of display classification");
            Check(BenchmarkReportExporter.BuildSatisfactionReport(completed with { Thermal = thermal! with { InterpretationVerified = false } }).Criteria.First().Result == "N/A", "Unverified SATA temperature does not satisfy a verified company thermal criterion");
            string json = Path.Combine(root, "company.json"); await BenchmarkReportExporter.ExportSatisfactionReport(completed, json, true);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(json));
            Check(doc.RootElement.GetProperty("Schema").GetString() == BenchmarkSession.ReportSchemaVersion && doc.RootElement.GetProperty("Serial").GetString() == "REDACTED" && doc.RootElement.GetProperty("Metrics").EnumerateArray().Any(m => m.GetProperty("Value").ValueKind == JsonValueKind.Null && m.GetProperty("Reason").ValueKind == JsonValueKind.String), "Clean JSON report preserves schema, redaction, available readings and missing-data reasons");
            string text = Path.Combine(root, "company.txt"); await BenchmarkReportExporter.ExportSatisfactionReport(completed, text);
            Check((await File.ReadAllTextAsync(text)).Contains("N/A -") && (await File.ReadAllTextAsync(text)).Contains("Company acceptance criteria only"), "Company text export keeps missing reasons and explicit separation from device health");
            var folderMapped = selected with { MountPoints = [root + Path.DirectorySeparatorChar] };
            bool refused = false; try { BenchmarkTargetResolver.Resolve(root, [folderMapped]); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Folder-mounted target is refused instead of incorrectly attributing host-root capacity and identity");
        }
        finally { Directory.Delete(root, true); }
    }
}
