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
using pixinit.Infrastructure.Logging;
using pixinit.ViewModels.Shared;

namespace pixinit.ViewModels.Benchmark;

public sealed partial class BenchmarkViewModel : ObservableObject
{
    internal Func<string, IReadOnlyList<StorageDevice>, BenchmarkTarget> TargetResolver { get; set; } = BenchmarkTargetResolver.Resolve;
    private readonly StorageOperationGate operationGate;
    private readonly Func<TbwInfo?> tbw;
    private readonly Func<ThermalInfo?> thermal;
    private readonly Func<double?> percentageUsed;
    private bool awaitingConsent;
    public bool PreparingDiagnostics { get; private set; }
    private readonly Func<CancellationToken, Task>? readDiagnostics;
    private readonly Func<bool> hasDiagnosticSnapshot;
    private readonly FileBenchmarkEngine engine;
    private readonly Func<IReadOnlyList<StorageDevice>> devices; private readonly Func<StorageDevice?> selectedDevice; private readonly Func<BenchmarkTemperature?> temperature; private readonly Func<bool> canStart; private readonly Action stateChanged;
    private CancellationTokenSource? cancellation; private long generation; private BenchmarkPreset preset; private BenchmarkSession? result;
    private string? consentSignature; internal IWriteConsent ConsentProvider { get; set; }
    public bool Busy { get; private set; }
    private string targetDirectory = "";
    public string TargetDirectory { get => targetDirectory; set { targetDirectory = value; ConfigurationChanged(); Changed(); Changed(nameof(TargetNotice)); TargetFreeBytes = null; TargetUsedPercent = null; _ = RefreshTargetSpaceAsync(); } }
    public IReadOnlyList<BenchmarkPreset> Presets { get; } = Enum.GetValues<BenchmarkPreset>();
    public BenchmarkPreset Preset { get => preset; set { if (preset == value) return; preset = value; ApplyPreset(); InvalidateConsent(); Changed(); } }
    public IReadOnlyList<int> FileSizes => UseUnbuffered ? [512, 1024, 2048] : [16, 32, 64, 128, 256, 512, 1024];
    public IReadOnlyList<int> Durations { get; } = Enumerable.Range(1, 30).ToArray();
    public bool IncludeRandom { get => RandomRead || RandomWrite; set { RandomRead = value; RandomWrite = value && IncludeWrites; ConfigurationChanged(); Changed(); } }
    public bool IncludeWrites { get => SequentialWrite; set { SequentialWrite = value; RandomWrite = value && RandomRead; if (!value) { SustainedWrite = false; Changed(nameof(SustainedWrite)); } ConfigurationChanged(); Changed(); } }
    private bool sustainedWrite;
    public bool SustainedWrite { get => sustainedWrite; set { sustainedWrite = value; ConfigurationChanged(); Changed(); } }
    public long SustainedBudgetMiB { get; set; } = 16384;
    private int sustainedDurationSeconds = 15;
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
    public string TbwDisplay => Tbw?.WrittenTB is double written ? $"{(Tbw.InterpretationVerified ? "" : "Unverified estimate: ")}{written:F3} TB host writes; {Tbw.RatedTB:F0} TB {(Tbw.RatingVerified ? "manufacturer-rated endurance" : "reference (rating unverified)" )}" : "Diagnostics not yet queried - Run Diagnostics Now";
    public string TbwRemainingDisplay => Tbw?.RemainingPercent is double remaining ? $"{TbwRemainingLabel}: {remaining:F1}%" : $"{TbwRemainingLabel}: unavailable";
    public string TbwRemainingLabel => Tbw?.RatingVerified == true ? "Rated endurance budget remaining" : "Reference remaining";
    public string TbwRatingNote => Tbw?.RatingVerified == true ? "Manufacturer TBW is an endurance rating, not a direct remaining-health measurement." : $"{Tbw?.RatedTB ?? 600:F0} TB is an unverified reference, not a device rating or remaining health.";
    public double TbwRemaining => Tbw?.RemainingPercent ?? 0;
    public string TbwState => HealthAssessor.EnduranceState(Tbw);
    public string TemperatureDisplay => Thermal?.ObservedC is double celsius ? $"{celsius:F1} °C; {Thermal.State}{(Thermal.InterpretationVerified ? "" : " (unverified SMART encoding)")}; observation {Thermal.ObservedAt?.ToLocalTime():g}" : "Diagnostics not yet queried - Run Diagnostics Now";
    public string TemperatureColor => Thermal?.State switch { ThermalState.Red => "#BA3548", ThermalState.Yellow => "#B57613", ThermalState.Green => "#247663", _ => "#68758C" };
    public bool DiagnosticsMissing => Tbw?.WrittenTB is null || Thermal?.ObservedC is null;
    public string DiagnosticContext => result is null ? "Selected drive diagnostic snapshot; idle/load conditions not established" : !SelectedContextMatchesResult ? "Target mapping is ambiguous or differs from the selected drive; diagnostic context cannot be attributed to this benchmark" : result.Tbw is null && result.Thermal is null ? "Current selected-drive diagnostic snapshot; not a benchmark load reading" : "Current benchmark diagnostic context; host writes and temperature are observed snapshots";
    public void RefreshDiagnosticContext()
    {
        NotifyPresentation();
        Changed(nameof(ProfileResults)); Changed(nameof(PassDetails)); Changed(nameof(ValidationNotice)); Changed(nameof(ModeLabel));
        RunDiagnosticsNowCommand?.Refresh();
        foreach (var name in new[] { nameof(Result), nameof(ResultSummary), nameof(ResultDetails), nameof(SequentialReadResult), nameof(SequentialWriteResult), nameof(RandomReadResult), nameof(RandomWriteResult), nameof(RandomReadProfileResult), nameof(RandomWriteProfileResult), nameof(RndReadMBs), nameof(RndWriteMBs), nameof(RndReadIOPS), nameof(RndWriteIOPS), nameof(SustainedWriteMBs), nameof(SustainedOverallDisplay), nameof(SustainedPostDropDisplay), nameof(DiagnosticsMissing), nameof(SustainedResult), nameof(Tbw), nameof(Thermal), nameof(TbwDisplay), nameof(TbwRemainingDisplay), nameof(TbwRemainingLabel), nameof(TbwRatingNote), nameof(TbwRemaining), nameof(TbwState), nameof(TemperatureDisplay), nameof(TemperatureColor), nameof(DiagnosticContext) }) Changed(name);
    }
    public bool SequentialRead { get; set; } = true; public bool SequentialWrite { get; set; } = true; public bool RandomRead { get; set; } = true; public bool RandomWrite { get; set; } = true;
    private int fileSizeMiB = 1024;
    public int FileSizeMiB { get => fileSizeMiB; set { fileSizeMiB = value; ConfigurationChanged(); Changed(); } } public int SequentialBlockKiB { get; set; } = 1024; public int RandomBlockKiB { get; set; } = 4; private int iterations = 3; public int Iterations { get => iterations; set { iterations = value; ConfigurationChanged(); Changed(); Changed(nameof(PassesExplanation)); } } public int RandomOperations { get; set; } = 65536;
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
    public string ConfigurationSummary { get { try { var c = Configuration(); return $"{c.FileSizeBytes / BenchmarkPolicy.MiB} MiB file · maximum application data writes {c.MaximumWriteBytes() / (double)BenchmarkPolicy.MiB:F1} MiB · {Iterations} passes · {(UseUnbuffered ? "unbuffered selected profiles" : "buffered QD1")} · sustained {(SustainedWrite ? $"up to {c.SustainedDurationSeconds}s / {c.SustainedMaximumWriteBytes / BenchmarkPolicy.MiB} MiB writes, 1 GiB circular file" : "not selected")}"; } catch (Exception ex) { return ex.Message; } } }
    public string ResultSummary => result?.Summary ?? "No benchmark result";
    public BenchmarkSession? Result => result;
    public string SequentialReadResult => SequentialProfileCard(BenchmarkOperation.SequentialRead);
    public string SequentialWriteResult => SequentialProfileCard(BenchmarkOperation.SequentialWrite);
    public string RandomReadResult => RandomReadProfileResult;
    public string RandomWriteResult => RandomWriteProfileResult;
    public string ResultDetails => Format(result);
    public ObservableCollection<OwnedFileRecord> OwnedFilesNeedingCleanup { get; } = new();
    public ActionCommand RunDiagnosticsNowCommand { get; } public ActionCommand BrowseCommand { get; } public ActionCommand ApplyConfigurationCommand { get; } public ActionCommand StartCommand { get; } public ActionCommand CancelCommand { get; } public ActionCommand RetryCleanupCommand { get; }

    public BenchmarkViewModel(FileBenchmarkEngine engine, Func<IReadOnlyList<StorageDevice>> devices, Func<StorageDevice?> selectedDevice, Action stateChanged, IWriteConsent? consent = null, Func<BenchmarkTemperature?>? temperature = null, Func<bool>? canStart = null, StorageOperationGate? operationGate = null, Func<TbwInfo?>? tbw = null, Func<ThermalInfo?>? thermal = null, Func<CancellationToken, Task>? readDiagnostics = null, Func<bool>? hasDiagnosticSnapshot = null, Action? showDiagnostics = null, Func<double?>? percentageUsed = null)
    {
        this.percentageUsed = percentageUsed ?? (() => null);
        this.readDiagnostics = readDiagnostics; this.hasDiagnosticSnapshot = hasDiagnosticSnapshot ?? (() => tbw?.Invoke() is not null || thermal?.Invoke() is not null);
        RunDiagnosticsNowCommand = new(() => showDiagnostics?.Invoke(), () => !Busy && !PreparingDiagnostics && selectedDevice() is not null);
        this.operationGate = operationGate ?? new(); this.tbw = tbw ?? (() => null); this.thermal = thermal ?? (() => null);
        this.engine = engine; this.devices = devices; this.selectedDevice = selectedDevice; this.stateChanged = stateChanged; this.temperature = temperature ?? (() => null); this.canStart = canStart ?? (() => true); ConsentProvider = consent ?? new WpfWriteConsent();
        BrowseCommand = new(Browse); ApplyConfigurationCommand = new(ConfigurationChanged); StartCommand = new(async () => await RunFromUiAsync(), () => !Busy && !PreparingDiagnostics && !awaitingConsent && !preflighting && this.canStart() && (Directory.Exists(TargetDirectory) || TargetDirectory == suggestedDirectory)); CancelCommand = new(Cancel, () => (Busy || PreparingDiagnostics || preflighting) && cancellation?.IsCancellationRequested == false);
        InitializePresentation();
        SetTestMode(false);
        RetryCleanupCommand = new(async () => await RetryCleanupAsync(), () => !Busy && OwnedFilesNeedingCleanup.Count > 0);
    }
    public Task InitializeAsync() => FindOwnedFilesAsync();
    public BenchmarkConfiguration Configuration()
    {
        BenchmarkOperation operations = BenchmarkOperation.None; if (SequentialRead) operations |= BenchmarkOperation.SequentialRead; if (SequentialWrite) operations |= BenchmarkOperation.SequentialWrite; if (RandomRead) operations |= BenchmarkOperation.RandomRead; if (RandomWrite) operations |= BenchmarkOperation.RandomWrite; if (SustainedWrite) operations |= BenchmarkOperation.SustainedWrite;
        return new(Preset, operations, checked(FileSizeMiB * BenchmarkPolicy.MiB), checked(SequentialBlockKiB * 1024), checked(RandomBlockKiB * 1024), Iterations, RandomOperations, 1) { Unbuffered = UseUnbuffered, TimedMode = UseUnbuffered && TimedMode, DurationSeconds = DurationSeconds, Profiles = SelectedProfiles, SustainedDurationSeconds = SustainedDurationSeconds, SustainedMaximumWriteBytes = checked(SustainedBudgetMiB * BenchmarkPolicy.MiB) };
    }
    public void ConfigurationChanged() { InvalidateConsent(); Changed(nameof(ConfigurationSummary)); StartCommand?.Refresh(); NotifyPresentation(); }
    private async Task RunFromUiAsync()
    {
        try { await RunAsync(); }
        catch (Exception ex) { Status = "Benchmark not started: " + ex.Message; Changed(nameof(Status)); }
    }
    public async Task RunAsync()
    {
        if (!StartCommand.CanExecute(null)) return;
        BlockingWarning = ""; NotifyPresentation();
        try
        {
            using var processLease = BenchmarkProcessLease.Acquire();
            await RunReviewedAsync();
        }
        catch (InvalidOperationException ex)
        {
            BlockingWarning = ex.Message; Status = "Benchmark not started: " + ex.Message;
            Changed(nameof(Status)); NotifyPresentation();
        }
    }
    private async Task RunReviewedAsync()
    {
        ValidateBenchmarkLocation(TargetDirectory);
        long reviewedGeneration = generation; string? reviewedDeviceId = selectedDevice()?.Id; string reviewedPath = TargetDirectory;
        if (!await PreflightAsync()) return;
        if (reviewedGeneration != generation || reviewedPath != TargetDirectory || Busy) return;
        if (SustainedWrite)
        {
            SustainedDurationSeconds = SmartDuration(SustainedDurationSeconds, TargetUsedPercent, TargetFreeBytes);
            if (SmartModeActive) SustainedBudgetMiB = Math.Min(SustainedBudgetMiB, 16384);
        }
        if (!Directory.Exists(TargetDirectory) && TargetDirectory == suggestedDirectory)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(TargetDirectory))!;
            var plannedTarget = TargetResolver(parent, devices());
            if (selectedDevice() is { } selected && (!plannedTarget.PhysicalIdentityReliable || plannedTarget.Device?.MatchKey != SnapshotFactory.DeviceSnapshot(selected).MatchKey)) throw new InvalidOperationException("Suggested folder is not mapped to the selected device.");
            ValidateStartSpace(plannedTarget.AvailableBytes);
            Directory.CreateDirectory(TargetDirectory);
        }
        var config = Configuration(); if (config.Unbuffered) { config = config with { FilePlan = BenchmarkFilePlan.Create() }; InvalidateConsent(); } var target = TargetResolver(TargetDirectory, devices()); ValidateStartSpace(target.AvailableBytes); BenchmarkPolicy.Validate(config, target.AvailableBytes);
        if (selectedDevice() is { } expected && (target.Device?.MatchKey != SnapshotFactory.DeviceSnapshot(expected).MatchKey || !target.PhysicalIdentityReliable)) throw new InvalidOperationException("Target volume is not reliably mapped to the selected device. Select its mounted volume; benchmark attribution refused.");
        string signature = target.Directory + "|" + config.Signature; bool required = config.HasWriteWork || config.HasReadWork;
        if (required && consentSignature != signature)
        {
            awaitingConsent = true; StartCommand.Refresh();
            try { if (!await ConsentProvider.RequestAsync(config, config.MaximumWriteBytes(), target)) { Status = "Benchmark not started · write consent declined"; Changed(nameof(Status)); return; } }
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
        BenchmarkProcessLease.EnsureNoCompetitor();
        var revalidated = TargetResolver(TargetDirectory, devices());
        if (reviewedGeneration != generation || reviewedDeviceId != selectedDevice()?.Id || !canStart() || Busy || Configuration().Signature != config.Signature || !SameTarget(target, revalidated) || signature != revalidated.Directory + "|" + config.Signature) { InvalidateConsent(); Status = "Benchmark not started · target volume or device mapping changed after consent; review and approve again"; Changed(nameof(Status)); return; }
        target = revalidated;
        ValidateStartSpace(target.AvailableBytes); BenchmarkPolicy.Validate(config, target.AvailableBytes);
        long run = ++generation; string? selectedId = selectedDevice()?.Id; using var source = new CancellationTokenSource(); cancellation = source; Busy = true; result = null; ResetDirectProgress(true); SustainedSamples.Clear(); RefreshDiagnosticContext(); SetProgress("Starting", 0, "Validating target and owned file"); Refresh();
        bool acceptingProgress = true;
        var progress = new Progress<BenchmarkProgress>(p => { if (run != generation || !acceptingProgress) return; if (p.Sample is { } sample) { SustainedSamples.Add(sample); Changed(nameof(SustainedWriteMBs)); Changed(nameof(SustainedOverallDisplay)); Changed(nameof(SustainedResult)); } UpdateDirectProgress(p); SetProgress(p.Phase + ": " + p.Stage, p.Percent, $"{p.BytesProcessed:N0} / {p.PlannedBytes:N0} bytes · {p.ElapsedSeconds:F2} s"); });
        try
        {
            BenchmarkProcessLease.EnsureNoCompetitor();
            using var lease = operationGate.Enter();
            bool contextMatches = target.PhysicalIdentityReliable && selectedDevice() is { } selected && SnapshotFactory.DeviceSnapshot(selected).MatchKey == target.Device?.MatchKey;
            var beforeTemperature = contextMatches ? temperature() : null;
            var beforeTbw = contextMatches ? tbw() : null;
            var beforeThermal = contextMatches ? thermal() : null;
            double? usedAtStart = TargetUsedPercent;
            long freeAtStart = target.AvailableBytes;
            var session = await Task.Run(() => engine.RunAsync(target, config, consentSignature == signature, progress, source.Token));
            session = session with { TargetVolumeUsedPercentAtStart = usedAtStart, TargetVolumeFreeBytesAtStart = freeAtStart };
            acceptingProgress = false;
            lease.Dispose();
            var afterCandidate = contextMatches ? temperature() : null;
            session = session with { PercentageUsed = contextMatches ? percentageUsed() : null, Tbw = beforeTbw, Thermal = contextMatches ? thermal() ?? beforeThermal : null, BeforeTemperature = beforeTemperature, AfterTemperature = afterCandidate?.ObservedAt > session.StartedUtc ? afterCandidate : null };
            if (run != generation || selectedDevice()?.Id != selectedId) { Status = "Benchmark result discarded because device selection changed"; Changed(nameof(Status)); return; }
            sessionElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(progressStarted).TotalSeconds; reportedWriteBytes = session.PreparationBytesWritten + session.Results.Where(r => r.Operation == BenchmarkOperation.SustainedWrite).Sum(r => r.BytesProcessed) + (session.Configuration.Unbuffered ? session.PassResults.Where(r => r.Measurement.Operation is BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite).Sum(r => r.Measurement.BytesProcessed) : session.Results.Where(r => r.Operation is BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite).Sum(r => r.BytesProcessed));
            result = session; if (session.Completion == BenchmarkCompletion.Completed) { OverallProgress = 100; Changed(nameof(OverallProgress)); } SustainedSamples.Clear(); foreach (var sample in session.SustainedSamples) SustainedSamples.Add(sample); RefreshDiagnosticContext(); Status = session.CleanupSucceeded ? $"Benchmark {session.Completion} · temporary file cleaned" : $"Benchmark {session.Completion} · cleanup requires attention";
            if (!session.CleanupSucceeded) await FindOwnedFilesAsync();
            SetProgress(session.Completion.ToString(), session.Completion == BenchmarkCompletion.Completed ? 100 : ProgressPercent, $"Session finished in {session.TotalSeconds:F2} s · {session.Results.Sum(r => r.BytesProcessed):N0} measured bytes");
            Changed(nameof(Result)); Changed(nameof(ResultSummary)); Changed(nameof(ResultDetails)); Changed(nameof(SequentialReadResult)); Changed(nameof(SequentialWriteResult)); Changed(nameof(RandomReadResult)); Changed(nameof(RandomWriteResult));
        }
        catch (Exception ex) { Error("Benchmark could not start", ex); }
        finally { acceptingProgress = false; Busy = false; cancellation = null; Refresh(); RefreshDiagnosticContext(); }
    }
    public void Cancel()
    {
        if (cancellation is not { IsCancellationRequested: false } source) return;
        Status = PreparingDiagnostics ? "Diagnostic preparation cancellation requested - waiting for read" : "Cancellation requested - stopping new I/O and cleaning the owned file";
        Changed(nameof(Status)); source.Cancel(); CancelCommand.Refresh();
    }
    public void SelectionChanged() { generation++; Cancel(); InvalidateConsent(); if (!Busy) { result = null; ResetDirectProgress(); SustainedSamples.Clear(); RefreshDiagnosticContext(); } }
    public void SetSuggestedTarget(StorageDevice? device)
    {
        if (Busy) return;
        string? path = device?.MountPoints.FirstOrDefault(p => Path.IsPathRooted(p) && string.Equals(Path.GetFullPath(p), Path.GetPathRoot(p), StringComparison.OrdinalIgnoreCase) && Directory.Exists(p));
        if (path is null) { suggestedDirectory = null; TargetDirectory = ""; return; }
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        suggestedDirectory = Path.Combine(string.Equals(Path.GetPathRoot(local), path, StringComparison.OrdinalIgnoreCase) ? local : path, "PIXINIT_TEST");
        TargetDirectory = suggestedDirectory; InvalidateConsent(); Changed(nameof(TargetDirectory)); Changed(nameof(ConfigurationSummary)); StartCommand?.Refresh();
    }
    public void Close() { generation++; Cancel(); }
    public async Task CancelAndWaitAsync() { Cancel(); while (Busy || PreparingDiagnostics || preflighting) await Task.Delay(20); }
    private void ApplyPreset() { var c = preset == BenchmarkPreset.Standard ? BenchmarkPolicy.Standard : BenchmarkPolicy.Quick; if (preset == BenchmarkPreset.Custom) return; FileSizeMiB = (int)(c.FileSizeBytes / BenchmarkPolicy.MiB); SequentialBlockKiB = c.SequentialBlockBytes / 1024; RandomBlockKiB = c.RandomBlockBytes / 1024; Iterations = c.Iterations; RandomOperations = c.RandomOperationCount; Changed(nameof(FileSizeMiB)); Changed(nameof(SequentialBlockKiB)); Changed(nameof(RandomBlockKiB)); Changed(nameof(Iterations)); Changed(nameof(RandomOperations)); Changed(nameof(ConfigurationSummary)); }
    private void InvalidateConsent() { consentSignature = null; Changed(nameof(ConsentStatus)); }
    private void Browse() { var dialog = new OpenFolderDialog { Title = "Choose filesystem folder for the PIXINIT benchmark file", InitialDirectory = Directory.Exists(TargetDirectory) ? TargetDirectory : null }; if (dialog.ShowDialog() == true) { TargetDirectory = dialog.FolderName; InvalidateConsent(); Changed(nameof(TargetDirectory)); Changed(nameof(ConfigurationSummary)); StartCommand?.Refresh(); _ = FindOwnedFilesAsync(); } }
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
    private void SetProgress(string stage, double percent, string detail) { CurrentOperation = stage; ProgressPercent = percent; ProgressDetail = detail; Changed(nameof(CurrentOperation)); Changed(nameof(ProgressPercent)); Changed(nameof(ProgressDetail)); }
    private void Refresh() { RunDiagnosticsNowCommand.Refresh(); Changed(nameof(Busy)); Changed(nameof(Status)); StartCommand.Refresh(); CancelCommand.Refresh(); stateChanged(); }
    private void Error(string message, Exception ex) { Status = $"{message}: {ex.Message}"; Changed(nameof(Status)); DiscoveryLog.Write(message, ex); }
    private static bool SameTarget(BenchmarkTarget a, BenchmarkTarget b) =>
        string.Equals(a.Directory, b.Directory, StringComparison.OrdinalIgnoreCase) && string.Equals(a.VolumeRoot, b.VolumeRoot, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.FileSystem, b.FileSystem, StringComparison.OrdinalIgnoreCase) && a.PhysicalIdentityReliable == b.PhysicalIdentityReliable && a.Device?.MatchKey == b.Device?.MatchKey;
    private static string Format(BenchmarkSession? s) => s is null ? "No selected benchmark session." : $"{s.Configuration.EffectiveIoMode}\n{s.ValidationWarning}\n{string.Join("; ", s.ProfileSummaries.Select(p => $"{p.Profile} {p.Operation}: average {p.AverageMBs:F2}, best {p.BestMBs:F2} MB/s ({p.CompletedPasses} passes)"))}\n{s.Completion} · {s.StartedUtc:O}\n{s.Target.Directory} · volume {s.Target.VolumeRoot} ({s.Target.FileSystem}) · {s.Target.MappingEvidence}\n{s.Configuration.Preset}: {s.Configuration.Operations}\nFile {s.Configuration.FileSizeBytes:N0} bytes; seq block {s.Configuration.SequentialBlockBytes:N0}; random block {s.Configuration.RandomBlockBytes:N0}; iterations {s.Configuration.Iterations}; random operations {s.Configuration.RandomOperationCount}; queue {s.Configuration.QueueDepth}\nI/O and flush: {s.Configuration.IoMode}\n" + string.Join("\n", s.Results.Select(r => $"{r.Operation}: {r.State}; {r.BytesProcessed:N0} bytes in {r.MeasuredSeconds:F4}s; {r.MegabytesPerSecond:F2} MB/s; {r.MebibytesPerSecond:F2} MiB/s; IOPS {(r.Iops?.ToString("F1") ?? "N/A")}; latency avg/min/max {(r.Latency is null ? "N/A" : $"{r.Latency.AverageMilliseconds:F3}/{r.Latency.MinimumMilliseconds:F3}/{r.Latency.MaximumMilliseconds:F3} ms")}")) + $"\nTemperature context: before {(s.BeforeTemperature?.Celsius is double before ? $"{before:F2} °C ({s.BeforeTemperature.Source}; {s.BeforeTemperature.Scope})" : "Unavailable")}; after {(s.AfterTemperature?.Celsius is double after ? $"{after:F2} °C ({s.AfterTemperature.Source}; {s.AfterTemperature.Scope})" : "Unavailable; no post-benchmark diagnostic reading")}" + $"\nPreparation {s.PreparationBytesWritten:N0} application data bytes / {s.PreparationSeconds:F3}s; cleanup {s.CleanupSeconds:F3}s ({(s.CleanupSucceeded ? "complete" : "failed")}); total {s.TotalSeconds:F3}s\n{s.Methodology}\nNo uncached device speed, durable-write latency, verified post-cache performance, or physical NAND writes are claimed. Results are affected by OS/filesystem/controller/SLC caches, encryption, thermal and power state, background activity, free space, firmware, test size, and workload. One run is not permanent capability.";
    private string ResultFor(BenchmarkOperation operation) { var r = result?.Results.FirstOrDefault(x => x.Operation == operation); if (result?.Configuration.Unbuffered == true) return "See per-profile averages above"; return r is null ? "Not run" : r.State != BenchmarkOperationState.Completed ? $"{r.State} · incomplete" : $"{r.MegabytesPerSecond:F1} MB/s · {r.MebibytesPerSecond:F1} MiB/s" + (r.Iops is double iops ? $" · {iops:F0} IOPS" : ""); }
}
