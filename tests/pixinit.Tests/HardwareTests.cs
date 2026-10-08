using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using pixinit.Application.Reporting;
using pixinit.Core.Assessment;
using pixinit.Core.History;
using pixinit.Infrastructure.History;
using pixinit.ViewModels.Shell;
using pixinit.Infrastructure.Windows.Sata;
using pixinit.Infrastructure.Windows.Nvme;
using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Benchmark;
using pixinit.Application.Benchmarking;

namespace pixinit.Tests;

internal static partial class Program
{
    private sealed class HardwareConsent : IWriteConsent { public int Calls; public Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes) { Calls++; return Task.FromResult(true); } }
    private static void HardwareTests()
    {
        Exception? failure = null;
        var app = new pixinit.App();
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                var window = app.MainWindow;
                var vm = (ShellViewModel)window.DataContext;
                var watch = Stopwatch.StartNew();
                while (vm.State is ScanState.NotStarted or ScanState.Running)
                {
                    if (watch.Elapsed > TimeSpan.FromSeconds(45)) throw new TimeoutException("Hardware discovery did not finish within validation budget.");
                    await Task.Delay(25);
                }
                Check(vm.State == ScanState.Completed, "Production startup automatically completes real discovery");
                var devices = vm.SataDevices.Concat(vm.NvmeDevices).Concat(vm.OtherDevices).ToArray();
                using var identity = WindowsIdentity.GetCurrent();
                bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                var report = new List<string>
                {
                    $"OS: {Environment.OSVersion}; process: {RuntimeInformation.ProcessArchitecture}; elevated token: {elevated}",
                    $"Initial automatic discovery: {watch.ElapsedMilliseconds} ms; disks: {devices.Length}; active tab: {(vm.ActiveTab == 0 ? "SATA" : "NVMe")}",
                    $"Window status: {vm.OperationStatus}"
                };
                Directory.CreateDirectory("docs");
                foreach (var d in devices)
                {
                    report.Add($"Disk {d.DiskNumber}: model={d.Model}; bus={d.Bus} ({d.NativeBusType}); protocol={d.Protocol}; media={d.Media}; capacityBytes={d.CapacityBytes}; firmware={d.Firmware ?? "unavailable"}; serialPresent={!string.IsNullOrWhiteSpace(d.Serial)}; removable={d.Removable}; system={d.IsUnambiguousSystemDisk}; access={d.Access}; mountCount={d.MountPoints.Count}; identityEvidence={d.IdentityEvidence}");
                    report.Add($"  Limitations: {d.Limitations}");
                }
                string? sata = vm.SelectedSata?.Id, nvme = vm.SelectedNvme?.Id;
                Check(vm.NvmeDevices.Count == 0 || vm.ActiveTab == 1, "Actual NVMe remains automatically selected");
                if (vm.SelectedNvme is not null)
                {
                    Check(vm.ReadNvmeCommand.CanExecute(null), "Explicit NVMe diagnostic action enabled for confirmed NVMe");
                    long beforeNvme = WindowsNvmeTransport.DispatchCount;
                    await vm.ReadNvmeAsync();
                    var first = vm.Nvme.Result ?? throw new InvalidOperationException("NVMe diagnostics did not publish a result.");
                    Check(first.Responses.Any(r => r.Succeeded), "Actual NVMe returns at least one validated protocol query");
                    Check(first.Responses.Single(r => r.Operation == Core.Diagnostics.Nvme.NvmeOperation.Namespace).Outcome == Core.Diagnostics.Nvme.NvmeOutcome.NotQueried, "Actual namespace query withheld without established namespace ID");
                    report.Add($"NVMe diagnostic read 1: native queries={WindowsNvmeTransport.DispatchCount - beforeNvme}; summary={first.Summary}");
                    foreach (var response in first.Responses) report.Add($"  {response.Operation}: {response.Outcome}; scope={response.Scope}; bytes={response.Payload.Length}; observed={response.ObservedAt:O}; reason={response.Explanation}");
                    report.Add($"  Displayed: warning={vm.Nvme.CriticalWarning}; temperature={vm.Nvme.Temperature}; spare={vm.Nvme.AvailableSpare}; spareThreshold={vm.Nvme.SpareThreshold}; percentageUsed={vm.Nvme.PercentageUsed}");
                    report.Add($"  Usage: {vm.Nvme.Usage.Replace(Environment.NewLine, " | ")}");
                    report.Add($"  Thermal: {vm.Nvme.Thermal.Replace(Environment.NewLine, " | ")}");
                    report.Add($"  Errors: {vm.Nvme.Errors.Replace(Environment.NewLine, " | ")}");
                    report.Add($"  Identity/capacity cross-check: discovery model={vm.SelectedNvme.Model}; firmware={vm.SelectedNvme.Firmware ?? "unavailable"}; capacityBytes={vm.SelectedNvme.CapacityBytes?.ToString() ?? "unavailable"}; Identify={vm.Nvme.Controller.Replace(Environment.NewLine, " | ")}");
                    var firstHealth = first.Responses.Single(r => r.Operation == Core.Diagnostics.Nvme.NvmeOperation.Health).Outcome;
                    await vm.ReadNvmeAsync();
                    var second = vm.Nvme.Result ?? throw new InvalidOperationException("Second NVMe result missing.");
                    Check(second.Responses.Single(r => r.Operation == Core.Diagnostics.Nvme.NvmeOperation.Health).Outcome == firstHealth, "Two manual NVMe reads have stable health-query availability");
                    Check(vm.SelectedNvme.Id == nvme && vm.ActiveTab == 1, "NVMe refresh preserves selection and active tab");
                    report.Add($"NVMe diagnostic read 2: summary={second.Summary}; health availability stable={firstHealth}");
                    var actualAssessment = second.Assessment ?? throw new InvalidOperationException("Actual NVMe assessment missing.");
                    Check(actualAssessment.Coverage == AssessmentCoverage.Complete && actualAssessment.Checklist != ChecklistResult.Inconclusive, "Actual NVMe checklist has complete mandatory evidence");
                    Check(actualAssessment.Checks.Any(c => c.RuleId == "NVME.COMPOSITE_TEMPERATURE" && c.Evidence.Contains("warning")) && actualAssessment.Checks.Any(c => c.RuleId == "NVME.MEDIA_ERRORS"), "Actual NVMe assessment exposes temperature-threshold and media-error reasons");
                    Check(second.Responses.Single(r => r.Operation == Core.Diagnostics.Nvme.NvmeOperation.Namespace).Outcome == Core.Diagnostics.Nvme.NvmeOutcome.NotQueried, "Actual assessment retains unavailable namespace details");
                    report.Add($"Actual assessment: state={actualAssessment.StateDisplay}; coverage={actualAssessment.Coverage}; checklist={actualAssessment.ChecklistDisplay}; rules={actualAssessment.RuleSetVersion}; scope={actualAssessment.ProtocolScope}");
                    foreach (var check in actualAssessment.Checks) report.Add($"  Check {check.RuleId}: {check.Outcome}; {check.Evidence}");

                    var probe = SnapshotFactory.Create(Guid.Empty, vm.SelectedNvme, second);
                    var reopenedStore = new SqliteHistoryStore(); await reopenedStore.InitializeAsync();
                    var rows = await reopenedStore.ListAsync(0, 20, "Nvme", probe.Device.MatchKey);
                    var completed = rows.Where(r => r.CompletionState == "Completed").Take(2).ToArray();
                    Check(completed.Length >= 2, "Two actual NVMe sessions persist and reopen from SQLite after store restart");
                    var current = await reopenedStore.LoadAsync(completed[0].Id) ?? throw new InvalidOperationException("Latest actual snapshot could not be reopened.");
                    var previous = await reopenedStore.LoadAsync(completed[1].Id) ?? throw new InvalidOperationException("Previous actual snapshot could not be reopened.");
                    var comparison = SnapshotComparison.Compare(previous, current);
                    Check(comparison.Count > 0, "Actual NVMe sessions produce a scope-compatible comparison");
                    string jsonPath = Path.GetFullPath("docs/phase-5-intel-nvme.json"), textPath = Path.GetFullPath("docs/phase-5-intel-nvme.txt");
                    await DiagnosticReportExporter.ExportJsonAsync(current, jsonPath, new());
                    await DiagnosticReportExporter.ExportTextAsync(current, textPath, new());
                    using var exported = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
                    var exportRoot = exported.RootElement;
                    Check(exportRoot.GetProperty("SerialRedacted").GetBoolean() && !exportRoot.GetProperty("RawPayloadsIncluded").GetBoolean() && exportRoot.GetProperty("Snapshot").GetProperty("Device").GetProperty("Serial").GetString() == "REDACTED", "Actual JSON export redacts serial and omits raw payloads by default");
                    var exportedMetrics = exportRoot.GetProperty("Snapshot").GetProperty("Metrics").EnumerateArray().ToDictionary(e => e.GetProperty("Key").GetString()!, e => e.GetProperty("ValueText").ValueKind == JsonValueKind.Null ? null : e.GetProperty("ValueText").GetString());
                    Check(current.Metrics.All(m => exportedMetrics.TryGetValue(m.Key, out var value) && value == m.ValueText), "Actual JSON exported values exactly match the selected snapshot");
                    Check((await File.ReadAllTextAsync(textPath)).Contains(current.RuleSetVersion), "Actual TXT export retains assessment rule version");
                    report.Add($"History: reopened {completed.Length} recent matching sessions; compatible metric comparisons={comparison.Count}; database={SqliteHistoryStore.DefaultPath}");
                    report.Add($"Exports: {jsonPath}; {textPath}; selected snapshot values matched JSON exactly; serial redacted; raw payloads omitted");

                    if (vm.History is not null)
                    {
                        vm.History.Selected = vm.History.Sessions.FirstOrDefault(r => r.Id == completed[0].Id);
                        await Task.Delay(100);
                    }
                }
                Check(WindowsAtaTransport.DispatchCount == 0, "Production startup sends zero ATA commands");
                Check(vm.SataDevices.Count != 0 || !vm.ReadSataCommand.CanExecute(null), "SATA action disabled when no SATA device exists");
                report.Add($"Native ATA dispatches: {WindowsAtaTransport.DispatchCount}; physical SATA diagnostics: NOT RUN (hardware harness performs discovery only)");

                if (vm.SelectedNvme is not null)
                {
                    var consent = new HardwareConsent(); vm.Benchmark.ConsentProvider = consent; vm.Benchmark.Preset = BenchmarkPreset.Quick;
                    vm.Benchmark.TargetDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); vm.Benchmark.ConfigurationChanged();
                    var actualTarget = BenchmarkTargetResolver.Resolve(vm.Benchmark.TargetDirectory, devices);
                    Check(actualTarget.PhysicalIdentityReliable && actualTarget.Device?.Model == vm.SelectedNvme.Model, "Actual benchmark filesystem maps to the selected Intel NVMe with reliable session evidence");
                    Check(vm.Benchmark.Configuration().MaximumWriteBytes() == 68 * BenchmarkPolicy.MiB, "Actual Quick workload is bounded to 68 MiB maximum writes per completed run");
                    if (window.FindName("HistoryTab") is TabItem oldHistory) oldHistory.IsSelected = false;
                    if (window.FindName("BenchmarkTab") is TabItem benchmarkExpander) benchmarkExpander.IsSelected = true;
                    window.Width = 1280; window.Height = 700; Pump(); Capture(window, "phase6-benchmark-config-laptop.png");

                    bool activeCaptured = false, cancellationRequested = false;
                    vm.Benchmark.PropertyChanged += (_, change) =>
                    {
                        if (!activeCaptured && vm.Benchmark.Busy && change.PropertyName == nameof(vm.Benchmark.ProgressPercent) && vm.Benchmark.ProgressPercent > 0) { activeCaptured = true; window.UpdateLayout(); Capture(window, "phase6-benchmark-active-laptop.png"); }
                        if (!cancellationRequested && vm.Benchmark.Busy && vm.Benchmark.CurrentOperation.Contains("Preparing") && vm.Benchmark.ProgressPercent >= 15) { cancellationRequested = true; vm.Benchmark.Cancel(); }
                    };
                    await vm.Benchmark.RunAsync(); var cancelledBenchmark = vm.Benchmark.Result ?? throw new InvalidOperationException("Cancelled benchmark result missing.");
                    Check(cancelledBenchmark.Completion == BenchmarkCompletion.Cancelled && cancelledBenchmark.CleanupSucceeded, "Actual benchmark cancellation preserves Cancelled state and cleans the owned file");
                    Check(activeCaptured && consent.Calls == 1, "Actual explicit write consent flow ran once and active benchmark UI was captured");

                    await vm.Benchmark.RunAsync(); var firstBenchmark = vm.Benchmark.Result ?? throw new InvalidOperationException("First completed benchmark missing.");
                    await vm.Benchmark.RunAsync(); var secondBenchmark = vm.Benchmark.Result ?? throw new InvalidOperationException("Second completed benchmark missing.");
                    Check(firstBenchmark.Completion == BenchmarkCompletion.Completed && secondBenchmark.Completion == BenchmarkCompletion.Completed && secondBenchmark.Results.Count == 4, "Two actual Quick sessions complete all four filesystem operations");
                    Check(secondBenchmark.Results.All(r => r.State == BenchmarkOperationState.Completed && r.BytesProcessed > 0 && r.MeasuredSeconds > 0) && secondBenchmark.Results.Where(r => r.Iops is not null).All(r => r.Iops > 0), "Actual sequential throughput, random IOPS and latency measurements are populated");
                    long actualWrites = new[] { cancelledBenchmark, firstBenchmark, secondBenchmark }.Sum(s => s.PreparationBytesWritten + s.Results.Where(r => r.Operation is BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite).Sum(r => r.BytesProcessed));
                    Check(actualWrites <= 2 * 68 * BenchmarkPolicy.MiB + 32 * BenchmarkPolicy.MiB, "Actual validation writes remain within all configured session allowances");
                    Check(new[] { cancelledBenchmark, firstBenchmark, secondBenchmark }.All(s => s.CleanupSucceeded && !File.Exists(s.BenchmarkFilePath)), "All actual benchmark files are cleaned after cancellation and completion");
                    Check(vm.Nvme.Result!.Responses.Single(r => r.Operation == Core.Diagnostics.Nvme.NvmeOperation.Namespace).Outcome == Core.Diagnostics.Nvme.NvmeOutcome.NotQueried, "Benchmarking does not invent unavailable NVMe namespace information");

                    var benchmarkStore = new SqliteHistoryStore(); await benchmarkStore.InitializeAsync(); var benchmarkRows = await benchmarkStore.ListBenchmarksAsync(0, 20, secondBenchmark.Target.Device?.MatchKey);
                    var twoCompleted = benchmarkRows.Where(r => r.CompletionState == "Completed").Take(2).ToArray(); Check(twoCompleted.Length == 2, "Actual benchmark history reopens after a new store instance");
                    var currentBenchmark = await benchmarkStore.LoadBenchmarkAsync(twoCompleted[0].Id) ?? throw new InvalidOperationException("Reopened benchmark missing."); var priorBenchmark = await benchmarkStore.LoadBenchmarkAsync(twoCompleted[1].Id) ?? throw new InvalidOperationException("Prior benchmark missing.");
                    Check(BenchmarkComparisons.Compare(priorBenchmark, currentBenchmark).Count == 4, "Actual compatible benchmark sessions compare all four operations");
                    string benchmarkJson = Path.GetFullPath("docs/phase-6-intel-nvme-benchmark.json"), benchmarkTxt = Path.GetFullPath("docs/phase-6-intel-nvme-benchmark.txt");
                    await BenchmarkReportExporter.ExportJsonAsync(currentBenchmark, benchmarkJson); await BenchmarkReportExporter.ExportTextAsync(currentBenchmark, benchmarkTxt);
                    using var benchmarkExport = JsonDocument.Parse(await File.ReadAllTextAsync(benchmarkJson)); var exportedResults = benchmarkExport.RootElement.GetProperty("Session").GetProperty("Results").EnumerateArray().ToArray();
                    Check(exportedResults.Select(e => e.GetProperty("BytesProcessed").GetInt64()).SequenceEqual(currentBenchmark.Results.Select(r => r.BytesProcessed)) && benchmarkExport.RootElement.GetProperty("Session").GetProperty("Target").GetProperty("Device").GetProperty("Serial").GetString() == "REDACTED", "Actual benchmark JSON values match history and serial is redacted");
                    Check((await File.ReadAllTextAsync(benchmarkTxt)).Contains(BenchmarkPolicy.Version), "Actual benchmark TXT includes the policy version and methodology");
                    Check(WindowsAtaTransport.DispatchCount == 0, "Actual filesystem benchmarks dispatch zero ATA mutation commands");
                    report.Add($"Phase 6 target: {currentBenchmark.Target.Directory}; {currentBenchmark.Target.MappingEvidence}; preset={currentBenchmark.Configuration.Preset}; policy={currentBenchmark.PolicyVersion}");
                    foreach (var r in currentBenchmark.Results) report.Add($"  {r.Operation}: bytes={r.BytesProcessed}; seconds={r.MeasuredSeconds:R}; MB/s={r.MegabytesPerSecond:R}; MiB/s={r.MebibytesPerSecond:R}; IOPS={r.Iops?.ToString("R") ?? "N/A"}; latency avg/min/max ms={r.Latency?.AverageMilliseconds:R}/{r.Latency?.MinimumMilliseconds:R}/{r.Latency?.MaximumMilliseconds:R}");
                    report.Add($"Phase 6 actual bytes written across cancelled + two completed sessions: {actualWrites}; configured upper bound: {2 * 68 * BenchmarkPolicy.MiB + 32 * BenchmarkPolicy.MiB}; all cleanup succeeded=true; consent prompts={consent.Calls}");
                    report.Add($"Phase 6 history reopened={twoCompleted.Length}; compatible comparisons={BenchmarkComparisons.Compare(priorBenchmark, currentBenchmark).Count}; exports={benchmarkTxt}, {benchmarkJson}");
                    window.Width = 1800; window.Height = 1000; Pump(); Capture(window, "phase6-benchmark-completed-desktop.png");
                    vm.Benchmark.SelectedHistoryRow = vm.Benchmark.Sessions.FirstOrDefault(r => r.Id == currentBenchmark.Id); await Task.Delay(100); Pump(); Capture(window, "phase6-benchmark-history-desktop.png");
                }
                window.Width = 1280; window.Height = 700; Pump();
                if (Find<pixinit.Views.Nvme.NvmeView>(window).Single().Content is ScrollViewer nvmeScroll) nvmeScroll.ScrollToVerticalOffset(235);
                Pump(); Capture(window, "phase5-assessment-laptop.png");
                window.Width = 1800; window.Height = 1000;
                if (Find<pixinit.Views.Nvme.NvmeView>(window).Single().Content is ScrollViewer desktopScroll) desktopScroll.ScrollToTop();
                if (window.FindName("HistoryTab") is TabItem history) history.IsSelected = true;
                Pump(); Capture(window, "phase5-history-desktop.png");
                int tab = vm.ActiveTab;
                int ticks = 0;
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1), DispatcherPriority.Normal, (_, _) => ticks++, app.Dispatcher);
                timer.Start(); watch.Restart();
                var scan = vm.ScanAsync();
                await app.Dispatcher.InvokeAsync(() => ticks++, DispatcherPriority.Normal);
                await scan; timer.Stop();
                Check(vm.State == ScanState.Completed, "Manual rescan completes on actual hardware");
                Check(vm.SelectedSata?.Id == sata && vm.SelectedNvme?.Id == nvme && vm.ActiveTab == tab, "Actual rescan preserves protocol selections");
                Check(ticks > 0, "Dispatcher processes work during native discovery");
                report.Add($"Rescan: {watch.ElapsedMilliseconds} ms; dispatcher timer ticks: {ticks}; selections preserved: true");
                await vm.StartInitialScanAsync();
                Check(vm.State == ScanState.Completed, "Automatic discovery entry is idempotent");
                Check(WindowsAtaTransport.DispatchCount == 0, "Real hardware rescans send zero ATA commands");
                report.Add($"Final native dispatch counters: NVMe={WindowsNvmeTransport.DispatchCount}; ATA={WindowsAtaTransport.DispatchCount}");
                File.WriteAllLines("docs/phase-6-hardware-observations.txt", report);
                foreach (string line in report) Console.WriteLine(line);
            }
            catch (Exception ex) { failure = ex; }
            finally { app.MainWindow?.Close(); app.Shutdown(); }
        }));
        app.Run();
        if (failure is not null) throw new InvalidOperationException("Hardware runtime verification failed", failure);
    }
}
