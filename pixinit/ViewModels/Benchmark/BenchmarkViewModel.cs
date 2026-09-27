using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using pixinit.Application.Benchmarking;
using pixinit.Application.Reporting;
using pixinit.Core.Benchmark;
using pixinit.Core.Devices;
using pixinit.Infrastructure.Benchmarking;
using pixinit.Infrastructure.History;
using pixinit.Infrastructure.Logging;
using pixinit.ViewModels.Shared;

namespace pixinit.ViewModels.Benchmark;

public sealed class BenchmarkViewModel : ObservableObject
{
    private readonly FileBenchmarkEngine engine; private readonly SqliteHistoryStore? store;
    private readonly Func<IReadOnlyList<StorageDevice>> devices; private readonly Func<StorageDevice?> selectedDevice; private readonly Action stateChanged;
    private CancellationTokenSource? cancellation; private long generation; private BenchmarkPreset preset; private BenchmarkSession? result, selectedHistory; private BenchmarkHistoryRow? selectedRow;
    private string? consentSignature; internal IWriteConsent ConsentProvider { get; set; }
    public bool Busy { get; private set; }
    public string TargetDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public IReadOnlyList<BenchmarkPreset> Presets { get; } = Enum.GetValues<BenchmarkPreset>();
    public BenchmarkPreset Preset { get => preset; set { if (preset == value) return; preset = value; ApplyPreset(); InvalidateConsent(); Changed(); } }
    public bool SequentialRead { get; set; } = true; public bool SequentialWrite { get; set; } = true; public bool RandomRead { get; set; } = true; public bool RandomWrite { get; set; } = true;
    public int FileSizeMiB { get; set; } = 32; public int SequentialBlockKiB { get; set; } = 1024; public int RandomBlockKiB { get; set; } = 4; public int Iterations { get; set; } = 1; public int RandomOperations { get; set; } = 1024;
    public string Status { get; private set; } = "Ready · choose a filesystem target and workload"; public string CurrentOperation { get; private set; } = "Idle";
    private double progressPercent;
    public double ProgressPercent
    {
        get => progressPercent;
        set { if (progressPercent == value) return; progressPercent = value; Changed(nameof(ProgressPercent)); }
    }
    public string ProgressDetail { get; private set; } = "0 bytes · 0 seconds";
    public string ConsentStatus => consentSignature is null ? "Write consent: not granted for this configuration" : "Write consent: granted for this exact configuration";
    public string ConfigurationSummary { get { try { var c = Configuration(); return $"{c.FileSizeBytes / BenchmarkPolicy.MiB} MiB file · maximum writes {c.MaximumWriteBytes() / (double)BenchmarkPolicy.MiB:F1} MiB · buffered · queue 1"; } catch (Exception ex) { return ex.Message; } } }
    public string ResultSummary => result?.Summary ?? "No benchmark result";
    public string ResultDetails => Format(result);
    public string HistoryDetail => Format(selectedHistory);
    public string Comparison { get; private set; } = "Select a completed historical benchmark to compare compatible methodology.";
    public ObservableCollection<BenchmarkHistoryRow> Sessions { get; } = new ObservableCollection<BenchmarkHistoryRow>();
    public BenchmarkHistoryRow? SelectedHistoryRow { get => selectedRow; set { selectedRow = value; Changed(); _ = LoadSelectedAsync(); ExportTextCommand.Refresh(); ExportJsonCommand.Refresh(); } }
    public ActionCommand BrowseCommand { get; } public ActionCommand ApplyConfigurationCommand { get; } public ActionCommand StartCommand { get; } public ActionCommand CancelCommand { get; } public ActionCommand LoadMoreCommand { get; } public ActionCommand ExportTextCommand { get; } public ActionCommand ExportJsonCommand { get; }
    private int historyOffset;

    public BenchmarkViewModel(FileBenchmarkEngine engine, SqliteHistoryStore? store, Func<IReadOnlyList<StorageDevice>> devices, Func<StorageDevice?> selectedDevice, Action stateChanged, IWriteConsent? consent = null)
    {
        this.engine = engine; this.store = store; this.devices = devices; this.selectedDevice = selectedDevice; this.stateChanged = stateChanged; ConsentProvider = consent ?? new WpfWriteConsent();
        BrowseCommand = new(Browse); ApplyConfigurationCommand = new(ConfigurationChanged); StartCommand = new(async () => await RunAsync(), () => !Busy && Directory.Exists(TargetDirectory)); CancelCommand = new(Cancel, () => Busy && cancellation?.IsCancellationRequested == false);
        LoadMoreCommand = new(async () => await LoadHistoryAsync(false)); ExportTextCommand = new(async () => await ExportAsync(false), () => selectedHistory is not null || result is not null); ExportJsonCommand = new(async () => await ExportAsync(true), () => selectedHistory is not null || result is not null);
    }
    public async Task InitializeAsync() { if (store is null) return; try { await store.InitializeAsync(); await LoadHistoryAsync(true); } catch (Exception ex) { Error("Benchmark history unavailable", ex); } }
    public BenchmarkConfiguration Configuration()
    {
        BenchmarkOperation operations = BenchmarkOperation.None; if (SequentialRead) operations |= BenchmarkOperation.SequentialRead; if (SequentialWrite) operations |= BenchmarkOperation.SequentialWrite; if (RandomRead) operations |= BenchmarkOperation.RandomRead; if (RandomWrite) operations |= BenchmarkOperation.RandomWrite;
        return new(Preset, operations, checked(FileSizeMiB * BenchmarkPolicy.MiB), checked(SequentialBlockKiB * 1024), checked(RandomBlockKiB * 1024), Iterations, RandomOperations, 1);
    }
    public void ConfigurationChanged() { InvalidateConsent(); Changed(nameof(ConfigurationSummary)); StartCommand.Refresh(); }
    public async Task RunAsync()
    {
        if (!StartCommand.CanExecute(null)) return; var config = Configuration(); var target = BenchmarkTargetResolver.Resolve(TargetDirectory, devices()); BenchmarkPolicy.Validate(config, target.AvailableBytes);
        string signature = target.Directory + "|" + config.Signature; bool required = config.HasWriteWork || config.HasReadWork;
        if (required && consentSignature != signature) { if (!await ConsentProvider.RequestAsync(config, config.MaximumWriteBytes())) { Status = "Benchmark not started · write consent declined"; Changed(nameof(Status)); return; } consentSignature = signature; Changed(nameof(ConsentStatus)); }
        long run = ++generation; string? selectedId = selectedDevice()?.Id; using var source = new CancellationTokenSource(); cancellation = source; Busy = true; result = null; SetProgress("Starting", 0, "Validating target and owned file"); Refresh();
        var progress = new Progress<BenchmarkProgress>(p => { if (run != generation) return; SetProgress(p.Stage, p.Percent, $"{p.BytesProcessed:N0} / {p.PlannedBytes:N0} bytes · {p.ElapsedSeconds:F2} s"); });
        try
        {
            var session = await engine.RunAsync(target, config, consentSignature == signature, progress, source.Token);
            if (run != generation || selectedDevice()?.Id != selectedId) { Status = "Benchmark result discarded because device selection changed"; Changed(nameof(Status)); return; }
            result = session; Status = session.CleanupSucceeded ? $"Benchmark {session.Completion} · temporary file cleaned" : $"Benchmark {session.Completion} · cleanup requires attention";
            if (store is not null) try { await store.SaveBenchmarkAsync(session); await LoadHistoryAsync(true); } catch (Exception ex) { Error("Benchmark visible · history save failed", ex); }
            Changed(nameof(ResultSummary)); Changed(nameof(ResultDetails)); ExportTextCommand.Refresh(); ExportJsonCommand.Refresh();
        }
        catch (Exception ex) { Error("Benchmark could not start", ex); }
        finally { Busy = false; cancellation = null; Refresh(); }
    }
    public void Cancel() { if (cancellation?.IsCancellationRequested == false) { cancellation.Cancel(); Status = "Cancellation requested · stopping new I/O and cleaning the owned file"; Changed(nameof(Status)); CancelCommand.Refresh(); } }
    public void SelectionChanged() { generation++; Cancel(); InvalidateConsent(); }
    public void SetSuggestedTarget(StorageDevice? device)
    {
        if (Busy || Directory.Exists(TargetDirectory) || device?.MountPoints.FirstOrDefault(Directory.Exists) is not string path) return;
        TargetDirectory = path; InvalidateConsent(); Changed(nameof(TargetDirectory)); Changed(nameof(ConfigurationSummary)); StartCommand.Refresh();
    }
    public void Close() { generation++; Cancel(); }
    private void ApplyPreset() { var c = preset == BenchmarkPreset.Standard ? BenchmarkPolicy.Standard : BenchmarkPolicy.Quick; if (preset == BenchmarkPreset.Custom) return; FileSizeMiB = (int)(c.FileSizeBytes / BenchmarkPolicy.MiB); SequentialBlockKiB = c.SequentialBlockBytes / 1024; RandomBlockKiB = c.RandomBlockBytes / 1024; Iterations = c.Iterations; RandomOperations = c.RandomOperationCount; Changed(nameof(FileSizeMiB)); Changed(nameof(SequentialBlockKiB)); Changed(nameof(RandomBlockKiB)); Changed(nameof(Iterations)); Changed(nameof(RandomOperations)); Changed(nameof(ConfigurationSummary)); }
    private void InvalidateConsent() { consentSignature = null; Changed(nameof(ConsentStatus)); }
    private void Browse() { var dialog = new OpenFolderDialog { Title = "Choose filesystem folder for the PIXINIT benchmark file", InitialDirectory = Directory.Exists(TargetDirectory) ? TargetDirectory : null }; if (dialog.ShowDialog() == true) { TargetDirectory = dialog.FolderName; InvalidateConsent(); Changed(nameof(TargetDirectory)); Changed(nameof(ConfigurationSummary)); StartCommand.Refresh(); } }
    private async Task LoadHistoryAsync(bool reset) { if (store is null) return; try { if (reset) { historyOffset = 0; Sessions.Clear(); } var rows = await store.ListBenchmarksAsync(historyOffset, 25); foreach (var r in rows) Sessions.Add(r); historyOffset += rows.Count; } catch (Exception ex) { Error("Benchmark history load failed", ex); } }
    private async Task LoadSelectedAsync()
    {
        if (store is null || selectedRow is null) return; try { selectedHistory = await store.LoadBenchmarkAsync(selectedRow.Id); Comparison = "No earlier compatible benchmark."; if (selectedHistory is { } current) { var rows = await store.ListBenchmarksAsync(0, 200, current.Target.Device?.MatchKey); foreach (var row in rows.Where(r => r.StartedUtc < current.StartedUtc)) if (await store.LoadBenchmarkAsync(row.Id) is { } prior) { var comparison = BenchmarkComparisons.Compare(prior, current); if (comparison.Count > 0) { Comparison = string.Join("\n", comparison.Select(c => $"{c.Operation}: {c.Previous:F2} → {c.Current:F2} MB/s; delta {c.Delta:+0.00;-0.00;0.00} MB/s ({c.PercentDelta:+0.00;-0.00;0.00}%). Small differences may not be statistically meaningful.")); break; } } } Changed(nameof(HistoryDetail)); Changed(nameof(Comparison)); ExportTextCommand.Refresh(); ExportJsonCommand.Refresh(); } catch (Exception ex) { Error("Benchmark history detail failed", ex); }
    }
    private async Task ExportAsync(bool json) { var s = selectedHistory ?? result; if (s is null) return; var dialog = new SaveFileDialog { Filter = json ? "JSON report (*.json)|*.json" : "Text report (*.txt)|*.txt", DefaultExt = json ? ".json" : ".txt", FileName = $"pixinit-benchmark-{s.StartedUtc:yyyyMMdd-HHmmss}" }; if (dialog.ShowDialog() != true) return; try { if (json) await BenchmarkReportExporter.ExportJsonAsync(s, dialog.FileName); else await BenchmarkReportExporter.ExportTextAsync(s, dialog.FileName); Status = "Benchmark report exported with serial redacted"; Changed(nameof(Status)); } catch (Exception ex) { Error("Benchmark export failed", ex); } }
    private void SetProgress(string stage, double percent, string detail) { CurrentOperation = stage; ProgressPercent = percent; ProgressDetail = detail; Changed(nameof(CurrentOperation)); Changed(nameof(ProgressPercent)); Changed(nameof(ProgressDetail)); }
    private void Refresh() { Changed(nameof(Busy)); Changed(nameof(Status)); StartCommand.Refresh(); CancelCommand.Refresh(); stateChanged(); }
    private void Error(string message, Exception ex) { Status = $"{message}: {ex.Message}"; Changed(nameof(Status)); DiscoveryLog.Write(message, ex); }
    private static string Format(BenchmarkSession? s) => s is null ? "No selected benchmark session." : $"{s.Completion} · {s.StartedUtc:O}\n{s.Target.Directory} · {s.Target.MappingEvidence}\n{s.Configuration.Preset}: {s.Configuration.Operations}\nFile {s.Configuration.FileSizeBytes:N0} bytes; seq block {s.Configuration.SequentialBlockBytes:N0}; random block {s.Configuration.RandomBlockBytes:N0}; iterations {s.Configuration.Iterations}; random operations {s.Configuration.RandomOperationCount}; queue {s.Configuration.QueueDepth}\n" + string.Join("\n", s.Results.Select(r => $"{r.Operation}: {r.State}; {r.BytesProcessed:N0} bytes in {r.MeasuredSeconds:F4}s; {r.MegabytesPerSecond:F2} MB/s; {r.MebibytesPerSecond:F2} MiB/s; IOPS {(r.Iops?.ToString("F1") ?? "N/A")}; latency avg/min/max {(r.Latency is null ? "N/A" : $"{r.Latency.AverageMilliseconds:F3}/{r.Latency.MinimumMilliseconds:F3}/{r.Latency.MaximumMilliseconds:F3} ms")}")) + $"\nPreparation {s.PreparationBytesWritten:N0} bytes / {s.PreparationSeconds:F3}s; cleanup {s.CleanupSeconds:F3}s ({(s.CleanupSucceeded ? "complete" : "failed")}); total {s.TotalSeconds:F3}s\n{s.Methodology}\nResults are affected by OS/filesystem/controller/SLC caches, encryption, thermal and power state, background activity, free space, firmware, test size, and workload. One run is not permanent capability.";
}
