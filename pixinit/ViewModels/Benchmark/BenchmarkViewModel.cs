using pixinit.Application.Scanning;
using pixinit.Application.Assessment;
using pixinit.Core.Diagnostics.Common;
using pixinit.Core.History;
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
    private readonly StorageOperationGate operationGate;
    private readonly Func<TbwInfo?> tbw;
    private readonly Func<ThermalInfo?> thermal;
    private readonly Func<DeviceViewModel?> deviceThermal;
    private readonly Func<CancellationToken, Task>? afterSustained;
    private readonly Func<double?> percentageUsed;
    internal Func<CancellationToken, Task> WaitForLoadCapture { get; set; } = token => Task.Delay(TimeSpan.FromSeconds(10), token);
    public DeviceViewModel? DeviceThermal => deviceThermal();
    public string IdleTemperatureDisplay => Thermal?.IdleC is double c ? $"Idle: {c:F1} C; observed {Thermal.IdleTempObservedAt?.ToLocalTime():g}" : "Idle: Unavailable - capture required";
    public string LoadTemperatureDisplay => Thermal?.LoadC is double c ? $"Load: {c:F1} C; observed {Thermal.LoadTempObservedAt?.ToLocalTime():g}; {Thermal.LoadCondition}" : "Load: Unavailable - capture required";
    public ActionCommand CaptureIdleCommand { get; }
    public ActionCommand CaptureLoadCommand { get; }
    public ActionCommand ExportSatisfactionCommand { get; }
    public ActionCommand ExportHistoryCommand { get; }
    private bool awaitingConsent;
    public bool PreparingDiagnostics { get; private set; }
    private readonly Func<CancellationToken, Task>? readDiagnostics;
    private readonly Func<bool> hasDiagnosticSnapshot;
    private readonly FileBenchmarkEngine engine; private readonly SqliteHistoryStore? store;
    private readonly Func<IReadOnlyList<StorageDevice>> devices; private readonly Func<StorageDevice?> selectedDevice; private readonly Func<BenchmarkTemperature?> temperature; private readonly Func<bool> canStart; private readonly Action stateChanged;
    private CancellationTokenSource? cancellation; private long generation; private BenchmarkPreset preset; private BenchmarkSession? result, selectedHistory; private BenchmarkHistoryRow? selectedRow;
    private string? consentSignature; internal IWriteConsent ConsentProvider { get; set; }
    public bool ExportRedactSerial { get; set; }
    public bool Busy { get; private set; }
    private string targetDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string TargetDirectory { get => targetDirectory; set { targetDirectory = value; ConfigurationChanged(); Changed(); } }
    public IReadOnlyList<BenchmarkPreset> Presets { get; } = Enum.GetValues<BenchmarkPreset>();
    public BenchmarkPreset Preset { get => preset; set { if (preset == value) return; preset = value; ApplyPreset(); InvalidateConsent(); Changed(); } }
    public IReadOnlyList<int> FileSizes { get; } = new[] { 16, 32, 64, 128, 256, 512, 1024 };
    public IReadOnlyList<int> Durations { get; } = Enumerable.Range(1, 30).ToArray();
    public bool IncludeRandom { get => RandomRead || RandomWrite; set { RandomRead = value; RandomWrite = value && IncludeWrites; ConfigurationChanged(); Changed(); } }
    public bool IncludeWrites { get => SequentialWrite; set { SequentialWrite = value; RandomWrite = value && RandomRead; if (!value) { SustainedWrite = false; Changed(nameof(SustainedWrite)); } ConfigurationChanged(); Changed(); } }
    private bool sustainedWrite;
    public bool SustainedWrite { get => sustainedWrite; set { sustainedWrite = value; ConfigurationChanged(); Changed(); } }
    public long SustainedBudgetMiB { get; set; } = 32768;
    private int sustainedDurationSeconds = 30;
    public int SustainedDurationSeconds { get => sustainedDurationSeconds; set { sustainedDurationSeconds = value; ConfigurationChanged(); Changed(); } }
    public ObservableCollection<SpeedSample> SustainedSamples { get; } = new();
    public double? RndReadMBs => result?.RndReadMBs;
    public double? RndWriteMBs => result?.RndWriteMBs;
    public double? RndReadIOPS => result?.RndReadIOPS;
    public double? RndWriteIOPS => result?.RndWriteIOPS;
    public double? SustainedWriteMBs
    {
        get
        {
            if (result is not null) return result.SustainedOverallMBs;
            double seconds = SustainedSamples.Sum(s => s.IntervalSeconds);
            return seconds > 0 ? SustainedSamples.Sum(s => (double)s.Bytes) / seconds / 1e6 : null;
        }
    }
    public string SustainedOverallDisplay => SustainedWriteMBs is double speed ? $"Overall: {speed:F1} MB/s" : "Overall: not run";
    public string SustainedPostDropDisplay => result?.SustainedPostDropMBs is double speed ? $"Post-drop: {speed:F1} MB/s" : "Post-drop: N/A";
    public string SustainedResult => result is null ? SustainedSamples.Count > 0 ? "Provisional overall from sampled bytes and actual intervals; post-drop not assessed until completion" : "Not run" : result.SustainedStatus + (result.SustainedPostDropMBs is double speed ? $"; after observed drop: {speed:F1} MB/s" : "");
    private bool SelectedContextMatchesResult => result is null || result.Target.PhysicalIdentityReliable && selectedDevice() is { } selected && SnapshotFactory.DeviceSnapshot(selected).MatchKey == result.Target.Device?.MatchKey;
    public TbwInfo? Tbw => result?.Tbw ?? (SelectedContextMatchesResult ? tbw() : null);
    public ThermalInfo? Thermal => result?.Thermal ?? (SelectedContextMatchesResult ? thermal() : null);
    public string TbwDisplay => Tbw?.WrittenTB is double written ? $"{(Tbw.InterpretationVerified ? "" : "Unverified estimate: ")}{written:F3} TB host writes; {Tbw.RatedTB:F0} TB reference (rating unverified)" : "Diagnostics not yet queried - Run Diagnostics Now";
    public double TbwRemaining => Tbw?.RemainingPercent ?? 0;
    public string TbwState => HealthAssessor.EnduranceState(Tbw);
    public string TemperatureDisplay => Thermal?.ObservedC is double celsius ? $"{celsius:F1} °C; {Thermal.State}{(Thermal.InterpretationVerified ? "" : " (unverified SMART encoding)")}; observation {Thermal.ObservedAt?.ToLocalTime():g}" : "Diagnostics not yet queried - Run Diagnostics Now";
    public string TemperatureColor => Thermal?.State switch { ThermalState.Red => "#BA3548", ThermalState.Yellow => "#B57613", ThermalState.Green => "#247663", _ => "#68758C" };
    public bool DiagnosticsMissing => Tbw?.WrittenTB is null || Thermal?.ObservedC is null;
    public string DiagnosticContext => result is null ? "Selected drive diagnostic snapshot; idle/load conditions not established" : !SelectedContextMatchesResult ? "Target mapping is ambiguous or differs from the selected drive; diagnostic context cannot be attributed to this benchmark" : result.Tbw is null && result.Thermal is null ? "Current selected-drive diagnostic snapshot; not a benchmark load reading" : "Saved benchmark diagnostic context; host writes and temperature are observed snapshots";
    public void RefreshDiagnosticContext()
    {
        RunDiagnosticsNowCommand?.Refresh(); CaptureIdleCommand?.Refresh(); CaptureLoadCommand?.Refresh(); ExportSatisfactionCommand?.Refresh();
        foreach (var name in new[] { nameof(Result), nameof(ResultSummary), nameof(ResultDetails), nameof(SequentialReadResult), nameof(SequentialWriteResult), nameof(RandomReadResult), nameof(RandomWriteResult), nameof(RndReadMBs), nameof(RndWriteMBs), nameof(RndReadIOPS), nameof(RndWriteIOPS), nameof(SustainedWriteMBs), nameof(SustainedOverallDisplay), nameof(SustainedPostDropDisplay), nameof(DiagnosticsMissing), nameof(SustainedResult), nameof(Tbw), nameof(Thermal), nameof(TbwDisplay), nameof(TbwRemaining), nameof(TbwState), nameof(TemperatureDisplay), nameof(DeviceThermal), nameof(IdleTemperatureDisplay), nameof(LoadTemperatureDisplay), nameof(TemperatureColor), nameof(DiagnosticContext) }) Changed(name);
    }
    public bool SequentialRead { get; set; } = true; public bool SequentialWrite { get; set; } = true; public bool RandomRead { get; set; } = true; public bool RandomWrite { get; set; } = true;
    private int fileSizeMiB = 32;
    public int FileSizeMiB { get => fileSizeMiB; set { fileSizeMiB = value; ConfigurationChanged(); Changed(); } } public int SequentialBlockKiB { get; set; } = 1024; public int RandomBlockKiB { get; set; } = 4; public int Iterations { get; set; } = 1; public int RandomOperations { get; set; } = 1000;
    public string Status { get; private set; } = "Ready · choose a filesystem target and workload"; public string CurrentOperation { get; private set; } = "Idle";
    private double progressPercent;
    public double ProgressPercent
    {
        get => progressPercent;
        set { if (progressPercent == value) return; progressPercent = value; Changed(nameof(ProgressPercent)); }
    }
    public string ProgressDetail { get; private set; } = "0 bytes · 0 seconds";
    public string ConsentStatus => consentSignature is null ? "Write consent: not granted for this configuration" : "Write consent: granted for this exact configuration";
    public const string BenchmarkLabel = "Buffered filesystem benchmark - QD1; caching affects results.";
    public string ConfigurationSummary { get { try { var c = Configuration(); return $"{c.FileSizeBytes / BenchmarkPolicy.MiB} MiB file · maximum application data writes {c.MaximumWriteBytes() / (double)BenchmarkPolicy.MiB:F1} MiB · QD1 · sustained {(SustainedWrite ? $"up to {c.SustainedDurationSeconds}s / {c.SustainedMaximumWriteBytes / BenchmarkPolicy.MiB} MiB writes, 1 GiB circular file" : "not selected")}"; } catch (Exception ex) { return ex.Message; } } }
    public string ResultSummary => result?.Summary ?? "No benchmark result";
    public BenchmarkSession? Result => result;
    public string SequentialReadResult => ResultFor(BenchmarkOperation.SequentialRead);
    public string SequentialWriteResult => ResultFor(BenchmarkOperation.SequentialWrite);
    public string RandomReadResult => ResultFor(BenchmarkOperation.RandomRead);
    public string RandomWriteResult => ResultFor(BenchmarkOperation.RandomWrite);
    public string ResultDetails => Format(result);
    public string HistoryDetail => Format(selectedHistory);
    public string Comparison { get; private set; } = "Select a completed historical benchmark to compare compatible methodology.";
    public ObservableCollection<BenchmarkHistoryRow> Sessions { get; } = new ObservableCollection<BenchmarkHistoryRow>();
    public ObservableCollection<OwnedFileRecord> OwnedFilesNeedingCleanup { get; } = new();
    public BenchmarkHistoryRow? SelectedHistoryRow { get => selectedRow; set { selectedRow = value; Changed(); _ = LoadSelectedAsync(); ExportTextCommand.Refresh(); ExportJsonCommand.Refresh(); } }
    public ActionCommand RunDiagnosticsNowCommand { get; } public ActionCommand BrowseCommand { get; } public ActionCommand ApplyConfigurationCommand { get; } public ActionCommand StartCommand { get; } public ActionCommand CancelCommand { get; } public ActionCommand RetryCleanupCommand { get; } public ActionCommand LoadMoreCommand { get; } public ActionCommand ExportTextCommand { get; } public ActionCommand ExportJsonCommand { get; }
    private int historyOffset;

    public BenchmarkViewModel(FileBenchmarkEngine engine, SqliteHistoryStore? store, Func<IReadOnlyList<StorageDevice>> devices, Func<StorageDevice?> selectedDevice, Action stateChanged, IWriteConsent? consent = null, Func<BenchmarkTemperature?>? temperature = null, Func<bool>? canStart = null, StorageOperationGate? operationGate = null, Func<TbwInfo?>? tbw = null, Func<ThermalInfo?>? thermal = null, Func<CancellationToken, Task>? readDiagnostics = null, Func<bool>? hasDiagnosticSnapshot = null, Action? showDiagnostics = null, Func<DeviceViewModel?>? deviceThermal = null, Func<CancellationToken, Task>? afterSustained = null, Func<double?>? percentageUsed = null, Action? showHistory = null)
    {
        this.deviceThermal = deviceThermal ?? (() => null); this.afterSustained = afterSustained; this.percentageUsed = percentageUsed ?? (() => null);
        CaptureIdleCommand = new(async () => await CaptureTemperatureAsync(true), () => !Busy && !PreparingDiagnostics && this.deviceThermal()?.CaptureIdleCommand.CanExecute(null) == true);
        CaptureLoadCommand = new(async () => await CaptureTemperatureAsync(false), () => !Busy && !PreparingDiagnostics && this.deviceThermal()?.CaptureLoadCommand.CanExecute(null) == true);
        ExportHistoryCommand = new(() => showHistory?.Invoke());
        ExportSatisfactionCommand = new(async () => await ExportSatisfactionAsync(), () => result is not null || selectedHistory is not null);
        this.readDiagnostics = readDiagnostics; this.hasDiagnosticSnapshot = hasDiagnosticSnapshot ?? (() => tbw?.Invoke() is not null || thermal?.Invoke() is not null);
        RunDiagnosticsNowCommand = new(() => showDiagnostics?.Invoke(), () => !Busy && !PreparingDiagnostics && selectedDevice() is not null);
        this.operationGate = operationGate ?? new(); this.tbw = tbw ?? (() => null); this.thermal = thermal ?? (() => null);
        this.engine = engine; this.store = store; this.devices = devices; this.selectedDevice = selectedDevice; this.stateChanged = stateChanged; this.temperature = temperature ?? (() => null); this.canStart = canStart ?? (() => true); ConsentProvider = consent ?? new WpfWriteConsent();
        BrowseCommand = new(Browse); ApplyConfigurationCommand = new(ConfigurationChanged); StartCommand = new(async () => await RunFromUiAsync(), () => !Busy && !PreparingDiagnostics && !awaitingConsent && this.canStart() && Directory.Exists(TargetDirectory)); CancelCommand = new(Cancel, () => (Busy || PreparingDiagnostics) && cancellation?.IsCancellationRequested == false);
        RetryCleanupCommand = new(async () => await RetryCleanupAsync(), () => !Busy && OwnedFilesNeedingCleanup.Count > 0);
        LoadMoreCommand = new(async () => await LoadHistoryAsync(false)); ExportTextCommand = new(async () => await ExportAsync(false), () => selectedHistory is not null || result is not null); ExportJsonCommand = new(async () => await ExportAsync(true), () => selectedHistory is not null || result is not null);
    }
    public async Task InitializeAsync() { await FindOwnedFilesAsync(); if (store is null) return; try { await store.InitializeAsync(); await LoadHistoryAsync(true); } catch (Exception ex) { Error("Benchmark history unavailable", ex); } }
    public BenchmarkConfiguration Configuration()
    {
        BenchmarkOperation operations = BenchmarkOperation.None; if (SequentialRead) operations |= BenchmarkOperation.SequentialRead; if (SequentialWrite) operations |= BenchmarkOperation.SequentialWrite; if (RandomRead) operations |= BenchmarkOperation.RandomRead; if (RandomWrite) operations |= BenchmarkOperation.RandomWrite; if (SustainedWrite) operations |= BenchmarkOperation.SustainedWrite;
        return new(Preset, operations, checked(FileSizeMiB * BenchmarkPolicy.MiB), checked(SequentialBlockKiB * 1024), checked(RandomBlockKiB * 1024), Iterations, RandomOperations, 1) { SustainedDurationSeconds = SustainedDurationSeconds, SustainedMaximumWriteBytes = checked(SustainedBudgetMiB * BenchmarkPolicy.MiB) };
    }
    public void ConfigurationChanged() { InvalidateConsent(); Changed(nameof(ConfigurationSummary)); StartCommand.Refresh(); }
    private async Task RunFromUiAsync()
    {
        try { await RunAsync(); }
        catch (Exception ex) { Status = "Benchmark not started: " + ex.Message; Changed(nameof(Status)); }
    }
    public async Task RunAsync()
    {
        if (!StartCommand.CanExecute(null)) return; long reviewedGeneration = generation; string? reviewedDeviceId = selectedDevice()?.Id; var config = Configuration(); var target = BenchmarkTargetResolver.Resolve(TargetDirectory, devices()); BenchmarkPolicy.Validate(config, target.AvailableBytes);
        string signature = target.Directory + "|" + config.Signature; bool required = config.HasWriteWork || config.HasReadWork;
        if (required && consentSignature != signature)
        {
            awaitingConsent = true; StartCommand.Refresh();
            try { if (!await ConsentProvider.RequestAsync(config, config.MaximumWriteBytes())) { Status = "Benchmark not started · write consent declined"; Changed(nameof(Status)); return; } }
            finally { awaitingConsent = false; StartCommand.Refresh(); }
            consentSignature = signature; Changed(nameof(ConsentStatus));
        }
        if (selectedDevice() is not null && !hasDiagnosticSnapshot() && readDiagnostics is not null)
        {
            using var preparation = new CancellationTokenSource();
            PreparingDiagnostics = true; cancellation = preparation;
            Status = "Reading selected drive diagnostics before benchmark"; Changed(nameof(Status)); Changed(nameof(PreparingDiagnostics)); Refresh();
            try { await readDiagnostics(preparation.Token); preparation.Token.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException) { Status = "Benchmark not started - diagnostic preparation cancelled"; Changed(nameof(Status)); return; }
            catch (Exception ex) { Status = "Diagnostic context unavailable: " + ex.Message; Changed(nameof(Status)); }
            finally { PreparingDiagnostics = false; cancellation = null; Changed(nameof(PreparingDiagnostics)); Refresh(); }
        }
        var revalidated = BenchmarkTargetResolver.Resolve(TargetDirectory, devices());
        if (reviewedGeneration != generation || reviewedDeviceId != selectedDevice()?.Id || !canStart() || Busy || Configuration().Signature != config.Signature || !SameTarget(target, revalidated) || signature != revalidated.Directory + "|" + config.Signature) { InvalidateConsent(); Status = "Benchmark not started · target volume or device mapping changed after consent; review and approve again"; Changed(nameof(Status)); return; }
        target = revalidated;
        BenchmarkPolicy.Validate(config, target.AvailableBytes);
        long run = ++generation; string? selectedId = selectedDevice()?.Id; using var source = new CancellationTokenSource(); cancellation = source; Busy = true; result = null; SustainedSamples.Clear(); RefreshDiagnosticContext(); SetProgress("Starting", 0, "Validating target and owned file"); Refresh();
        bool acceptingProgress = true;
        var progress = new Progress<BenchmarkProgress>(p => { if (run != generation || !acceptingProgress) return; if (p.Sample is { } sample) { SustainedSamples.Add(sample); Changed(nameof(SustainedWriteMBs)); Changed(nameof(SustainedOverallDisplay)); Changed(nameof(SustainedResult)); } SetProgress(p.Phase + ": " + p.Stage, p.Percent, $"{p.BytesProcessed:N0} / {p.PlannedBytes:N0} bytes · {p.ElapsedSeconds:F2} s"); });
        try
        {
            using var lease = operationGate.Enter();
            bool contextMatches = target.PhysicalIdentityReliable && selectedDevice() is { } selected && SnapshotFactory.DeviceSnapshot(selected).MatchKey == target.Device?.MatchKey;
            var beforeTemperature = contextMatches ? temperature() : null;
            var beforeTbw = contextMatches ? tbw() : null;
            var beforeThermal = contextMatches ? thermal() : null;
            var session = await Task.Run(() => engine.RunAsync(target, config, consentSignature == signature, progress, source.Token));
            acceptingProgress = false;
            lease.Dispose();
            deviceThermal()?.MarkBenchmarkFinished(session.FinishedUtc);
            string? loadStatus = null;
            if (contextMatches && session.Completion == BenchmarkCompletion.Completed && session.Results.Any(r => r.Operation == BenchmarkOperation.SustainedWrite && r.State == BenchmarkOperationState.Completed) && afterSustained is not null)
            {
                try
                {
                    Status = "Benchmark complete - waiting ten seconds for post-sustained temperature capture"; Changed(nameof(Status));
                    await WaitForLoadCapture(source.Token); source.Token.ThrowIfCancellationRequested();
                    if (run == generation && selectedDevice()?.Id == selectedId)
                    {
                        var captureStart = DateTimeOffset.UtcNow;
                        if (captureStart - session.FinishedUtc <= TimeSpan.FromSeconds(60)) await afterSustained(source.Token);
                        else loadStatus = "Post-sustained capture missed the 60 second start window";
                    }
                }
                catch (OperationCanceledException) { loadStatus = "Post-sustained capture cancelled; completed benchmark retained"; }
                catch (Exception ex) { loadStatus = "Post-sustained capture unavailable: " + ex.Message; }
            }
            var afterCandidate = contextMatches ? temperature() : null;
            session = session with { PercentageUsed = contextMatches ? percentageUsed() : null, Tbw = beforeTbw, Thermal = contextMatches ? thermal() ?? beforeThermal : null, BeforeTemperature = beforeTemperature, AfterTemperature = afterCandidate?.ObservedAt > session.StartedUtc ? afterCandidate : null };
            if (run != generation || selectedDevice()?.Id != selectedId) { Status = "Benchmark result discarded because device selection changed"; Changed(nameof(Status)); return; }
            result = session; SustainedSamples.Clear(); foreach (var sample in session.SustainedSamples) SustainedSamples.Add(sample); RefreshDiagnosticContext(); Status = session.CleanupSucceeded ? $"Benchmark {session.Completion} · temporary file cleaned" : $"Benchmark {session.Completion} · cleanup requires attention";
            if (loadStatus is not null) Status += "; " + loadStatus;
            if (!session.CleanupSucceeded) await FindOwnedFilesAsync();
            SetProgress(session.Completion.ToString(), session.Completion == BenchmarkCompletion.Completed ? 100 : ProgressPercent, $"Session finished in {session.TotalSeconds:F2} s · {session.Results.Sum(r => r.BytesProcessed):N0} measured bytes");
            if (store is not null) try { await store.SaveBenchmarkAsync(session); await LoadHistoryAsync(true); } catch (Exception ex) { Error("Benchmark visible · history save failed", ex); }
            Changed(nameof(Result)); Changed(nameof(ResultSummary)); Changed(nameof(ResultDetails)); Changed(nameof(SequentialReadResult)); Changed(nameof(SequentialWriteResult)); Changed(nameof(RandomReadResult)); Changed(nameof(RandomWriteResult)); ExportTextCommand.Refresh(); ExportJsonCommand.Refresh();
        }
        catch (Exception ex) { Error("Benchmark could not start", ex); }
        finally { acceptingProgress = false; Busy = false; cancellation = null; Refresh(); RefreshDiagnosticContext(); }
    }
    private async Task CaptureTemperatureAsync(bool idle)
    {
        if (deviceThermal() is not { } vm) return;
        await vm.CaptureTemperatureAsync(idle);
        if (result is { } saved && SelectedContextMatchesResult)
        {
            result = saved with { Thermal = vm.ThermalSnapshot };
            if (store is not null) try { await store.SaveBenchmarkAsync(result); } catch (Exception ex) { Error("Temperature visible - history update failed", ex); }
        }
        RefreshDiagnosticContext();
    }
    private async Task ExportSatisfactionAsync()
    {
        var session = selectedHistory ?? result; if (session is null) return;
        var dialog = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt|JSON report (*.json)|*.json", DefaultExt = ".txt", FileName = $"pixinit-company-report-{session.StartedUtc:yyyyMMdd-HHmmss}" };
        if (dialog.ShowDialog() != true) return;
        try { await BenchmarkReportExporter.ExportSatisfactionReport(session, dialog.FileName, Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase), ExportRedactSerial); Status = "Company criteria report exported; criteria are separate from device health assessment"; Changed(nameof(Status)); }
        catch (Exception ex) { Error("Company report export failed", ex); }
    }
    public void Cancel()
    {
        if (cancellation is not { IsCancellationRequested: false } source) return;
        Status = PreparingDiagnostics ? "Diagnostic preparation cancellation requested - waiting for read" : "Cancellation requested - stopping new I/O and cleaning the owned file";
        Changed(nameof(Status)); source.Cancel(); CancelCommand.Refresh();
    }
    public void SelectionChanged() { generation++; Cancel(); InvalidateConsent(); if (!Busy) { result = null; SustainedSamples.Clear(); RefreshDiagnosticContext(); } }
    public void SetSuggestedTarget(StorageDevice? device)
    {
        if (Busy || Directory.Exists(TargetDirectory) || device?.MountPoints.FirstOrDefault(Directory.Exists) is not string path) return;
        TargetDirectory = path; InvalidateConsent(); Changed(nameof(TargetDirectory)); Changed(nameof(ConfigurationSummary)); StartCommand.Refresh();
    }
    public void Close() { generation++; Cancel(); }
    public async Task CancelAndWaitAsync() { Cancel(); while (Busy || PreparingDiagnostics) await Task.Delay(20); }
    private void ApplyPreset() { var c = preset == BenchmarkPreset.Standard ? BenchmarkPolicy.Standard : BenchmarkPolicy.Quick; if (preset == BenchmarkPreset.Custom) return; FileSizeMiB = (int)(c.FileSizeBytes / BenchmarkPolicy.MiB); SequentialBlockKiB = c.SequentialBlockBytes / 1024; RandomBlockKiB = c.RandomBlockBytes / 1024; Iterations = c.Iterations; RandomOperations = c.RandomOperationCount; Changed(nameof(FileSizeMiB)); Changed(nameof(SequentialBlockKiB)); Changed(nameof(RandomBlockKiB)); Changed(nameof(Iterations)); Changed(nameof(RandomOperations)); Changed(nameof(ConfigurationSummary)); }
    private void InvalidateConsent() { consentSignature = null; Changed(nameof(ConsentStatus)); }
    private void Browse() { var dialog = new OpenFolderDialog { Title = "Choose filesystem folder for the PIXINIT benchmark file", InitialDirectory = Directory.Exists(TargetDirectory) ? TargetDirectory : null }; if (dialog.ShowDialog() == true) { TargetDirectory = dialog.FolderName; InvalidateConsent(); Changed(nameof(TargetDirectory)); Changed(nameof(ConfigurationSummary)); StartCommand.Refresh(); _ = FindOwnedFilesAsync(); } }
    private async Task FindOwnedFilesAsync()
    {
        try
        {
            OwnedFilesNeedingCleanup.Clear();
            if (Directory.Exists(TargetDirectory)) foreach (var record in await engine.FindOwnedFilesAsync(TargetDirectory)) OwnedFilesNeedingCleanup.Add(record);
            if (OwnedFilesNeedingCleanup.Count > 0) { Status = $"{OwnedFilesNeedingCleanup.Count} verified PIXINIT benchmark file(s) require cleanup in {TargetDirectory}"; Changed(nameof(Status)); }
        }
        catch (Exception ex) { Error("Owned benchmark cleanup scan failed", ex); }
        finally { RetryCleanupCommand?.Refresh(); }
    }
    private async Task RetryCleanupAsync()
    {
        int cleaned = 0; string? error = null;
        foreach (var record in OwnedFilesNeedingCleanup.ToArray()) try { await engine.CleanupOwnedFileAsync(record); cleaned++; } catch (Exception ex) { error = ex.Message; DiscoveryLog.Write("Owned benchmark file cleanup retry failed", ex); }
        await FindOwnedFilesAsync();
        Status = OwnedFilesNeedingCleanup.Count == 0 ? $"Cleaned {cleaned} verified PIXINIT benchmark file(s)" : $"Cleanup incomplete; {OwnedFilesNeedingCleanup.Count} verified file(s) remain at {TargetDirectory}: {error}";
        Changed(nameof(Status));
    }
    private async Task LoadHistoryAsync(bool reset) { if (store is null) return; try { if (reset) { historyOffset = 0; Sessions.Clear(); } var rows = await store.ListBenchmarksAsync(historyOffset, 25); foreach (var r in rows) Sessions.Add(r); historyOffset += rows.Count; } catch (Exception ex) { Error("Benchmark history load failed", ex); } }
    private async Task LoadSelectedAsync()
    {
        if (store is null || selectedRow is null) return; try { selectedHistory = await store.LoadBenchmarkAsync(selectedRow.Id); Comparison = "No earlier compatible benchmark."; if (selectedHistory is { } current) { var rows = await store.ListBenchmarksAsync(0, 200, current.Target.Device?.MatchKey); foreach (var row in rows.Where(r => r.StartedUtc < current.StartedUtc)) if (await store.LoadBenchmarkAsync(row.Id) is { } prior) { var comparison = BenchmarkComparisons.Compare(prior, current); if (comparison.Count > 0) { Comparison = string.Join("\n", comparison.Select(c => $"{c.Operation}: {c.Previous:F2} → {c.Current:F2} MB/s; delta {c.Delta:+0.00;-0.00;0.00} MB/s ({c.PercentDelta:+0.00;-0.00;0.00}%). Small differences may not be statistically meaningful.")); break; } } } Changed(nameof(HistoryDetail)); Changed(nameof(Comparison)); ExportTextCommand.Refresh(); ExportJsonCommand.Refresh(); } catch (Exception ex) { Error("Benchmark history detail failed", ex); }
    }
    private async Task ExportAsync(bool json) { var s = selectedHistory ?? result; if (s is null) return; var dialog = new SaveFileDialog { Filter = json ? "JSON report (*.json)|*.json" : "Text report (*.txt)|*.txt", DefaultExt = json ? ".json" : ".txt", FileName = $"pixinit-benchmark-{s.StartedUtc:yyyyMMdd-HHmmss}" }; if (dialog.ShowDialog() != true) return; try { if (json) await BenchmarkReportExporter.ExportJsonAsync(s, dialog.FileName, ExportRedactSerial); else await BenchmarkReportExporter.ExportTextAsync(s, dialog.FileName, ExportRedactSerial); Status = ExportRedactSerial ? "Benchmark report exported with serial redacted" : "Benchmark report exported with full serial"; Changed(nameof(Status)); } catch (Exception ex) { Error("Benchmark export failed", ex); } }
    private void SetProgress(string stage, double percent, string detail) { CurrentOperation = stage; ProgressPercent = percent; ProgressDetail = detail; Changed(nameof(CurrentOperation)); Changed(nameof(ProgressPercent)); Changed(nameof(ProgressDetail)); }
    private void Refresh() { RunDiagnosticsNowCommand.Refresh(); Changed(nameof(Busy)); Changed(nameof(Status)); StartCommand.Refresh(); CancelCommand.Refresh(); stateChanged(); }
    private void Error(string message, Exception ex) { Status = $"{message}: {ex.Message}"; Changed(nameof(Status)); DiscoveryLog.Write(message, ex); }
    private static bool SameTarget(BenchmarkTarget a, BenchmarkTarget b) =>
        string.Equals(a.Directory, b.Directory, StringComparison.OrdinalIgnoreCase) && string.Equals(a.VolumeRoot, b.VolumeRoot, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.FileSystem, b.FileSystem, StringComparison.OrdinalIgnoreCase) && a.PhysicalIdentityReliable == b.PhysicalIdentityReliable && a.Device?.MatchKey == b.Device?.MatchKey;
    private static string Format(BenchmarkSession? s) => s is null ? "No selected benchmark session." : $"{BenchmarkLabel}\n{s.Completion} · {s.StartedUtc:O}\n{s.Target.Directory} · volume {s.Target.VolumeRoot} ({s.Target.FileSystem}) · {s.Target.MappingEvidence}\n{s.Configuration.Preset}: {s.Configuration.Operations}\nFile {s.Configuration.FileSizeBytes:N0} bytes; seq block {s.Configuration.SequentialBlockBytes:N0}; random block {s.Configuration.RandomBlockBytes:N0}; iterations {s.Configuration.Iterations}; random operations {s.Configuration.RandomOperationCount}; queue {s.Configuration.QueueDepth}\nI/O and flush: {s.Configuration.IoMode}\n" + string.Join("\n", s.Results.Select(r => $"{r.Operation}: {r.State}; {r.BytesProcessed:N0} bytes in {r.MeasuredSeconds:F4}s; {r.MegabytesPerSecond:F2} MB/s; {r.MebibytesPerSecond:F2} MiB/s; IOPS {(r.Iops?.ToString("F1") ?? "N/A")}; latency avg/min/max {(r.Latency is null ? "N/A" : $"{r.Latency.AverageMilliseconds:F3}/{r.Latency.MinimumMilliseconds:F3}/{r.Latency.MaximumMilliseconds:F3} ms")}")) + $"\nTemperature context: before {(s.BeforeTemperature?.Celsius is double before ? $"{before:F2} °C ({s.BeforeTemperature.Source}; {s.BeforeTemperature.Scope})" : "Unavailable")}; after {(s.AfterTemperature?.Celsius is double after ? $"{after:F2} °C ({s.AfterTemperature.Source}; {s.AfterTemperature.Scope})" : "Unavailable; no post-benchmark diagnostic reading")}" + $"\nPreparation {s.PreparationBytesWritten:N0} application data bytes / {s.PreparationSeconds:F3}s; cleanup {s.CleanupSeconds:F3}s ({(s.CleanupSucceeded ? "complete" : "failed")}); total {s.TotalSeconds:F3}s\n{s.Methodology}\nNo uncached device speed, durable-write latency, verified post-cache performance, or physical NAND writes are claimed. Results are affected by OS/filesystem/controller/SLC caches, encryption, thermal and power state, background activity, free space, firmware, test size, and workload. One run is not permanent capability.";
    private string ResultFor(BenchmarkOperation operation) { var r = result?.Results.FirstOrDefault(x => x.Operation == operation); return r is null ? "Not run" : r.State != BenchmarkOperationState.Completed ? $"{r.State} · incomplete" : $"{r.MegabytesPerSecond:F1} MB/s · {r.MebibytesPerSecond:F1} MiB/s" + (r.Iops is double iops ? $" · {iops:F0} IOPS" : ""); }
}
