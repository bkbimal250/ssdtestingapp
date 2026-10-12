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
        var disk = Drive("current-temperature", StorageProtocol.Nvme);
        var currentDevice = new CaptureDevice(); currentDevice.SetDevice(disk);
        currentDevice.Reading = new(null, null, ThermalState.Green, 30, "Current test observation", DateTimeOffset.UtcNow);
        Check(currentDevice.CurrentTemp == 30 && currentDevice.ThermalSnapshot?.LoadC is null && currentDevice.ThermalSnapshot?.IdleC is null, "Current temperature remains available without inventing captured idle/load conditions");
        Check(typeof(pixinit.ViewModels.Shared.DeviceViewModel).GetProperty("CaptureIdleCommand") is null && typeof(BenchmarkViewModel).GetProperty("CaptureLoadCommand") is null, "Manual temperature capture commands are removed from view models");
        string root = Path.Combine(Path.GetTempPath(), "pixinit-company-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var selected = disk with { MountPoints = [Path.GetPathRoot(root)!], InterfacePath = "test", InstanceId = "test" };
            var gate = new StorageOperationGate(); var engine = new FileBenchmarkEngine(); var config = BenchmarkPolicy.Quick with { Operations = BenchmarkOperation.SustainedWrite, SustainedMaximumWriteBytes = BenchmarkPolicy.MiB, SustainedDurationSeconds = 1 };
            ThermalInfo? thermal = new(null, null, ThermalState.Green, 35, "Injected", DateTimeOffset.UtcNow); 
            var vm = new BenchmarkViewModel(engine, () => [selected], () => selected, () => { }, new Consent(true), operationGate: gate, thermal: () => thermal,
                percentageUsed: () => 20)
            { TargetResolver = WorkflowTestTarget, UseUnbuffered = false, TargetDirectory = root, SequentialRead = false, SequentialWrite = false, RandomRead = false, RandomWrite = false, SustainedWrite = true, SustainedBudgetMiB = 1, SustainedDurationSeconds = 1 };
            await vm.RunAsync(); var session = vm.Result!;
            Check(session.Thermal?.ObservedC == 35 && session.Thermal.LoadC is null && session.PercentageUsed == 20 && session.CleanupSucceeded, "Bounded sustained run keeps current thermal context without additional capture or delayed completion");
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
