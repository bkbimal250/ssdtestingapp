using System.Diagnostics;
using System.IO;
using pixinit.Core.Benchmark;
using pixinit.ViewModels.Shared;

namespace pixinit.ViewModels.Benchmark;

public sealed partial class BenchmarkViewModel
{
    public sealed record ResultCard(string Average, string Best, string Profile, string PassTooltip)
    { public string MeasuredAverage { get; init; } = ""; }
    public ActionCommand QuickModeCommand { get; private set; } = null!;
    public ActionCommand DeepModeCommand { get; private set; } = null!;
    public ActionCommand CustomModeCommand { get; private set; } = null!;
    public ActionCommand Size512Command { get; private set; } = null!;
    public ActionCommand Size1024Command { get; private set; } = null!;
    public ActionCommand Size2048Command { get; private set; } = null!;
    private bool preflighting;
    private string benchmarkMode = "Quick";
    private string? suggestedDirectory;
    private long targetSpaceGeneration;
    public string BenchmarkMode => benchmarkMode;
    public bool AdvancedOpen { get; set; }
    public bool ConfigurationEditable => !Busy && !PreparingDiagnostics && !awaitingConsent && !preflighting;
    public long? TargetFreeBytes { get; private set; }
    public double? TargetUsedPercent { get; private set; }
    public string TargetSpaceDisplay => TargetFreeBytes is long bytes ? $"Free: {bytes / 1073741824d:F1} GiB" + (TargetUsedPercent is double used ? $" | {used:F1}% used" : "") : "Free space will be checked before starting";
    public string TargetSpaceColor => TargetFreeBytes < 10L * 1073741824 ? "#B91C1C" : "#475569";
    public bool SmartModeActive => TargetUsedPercent > 80 || TargetFreeBytes < 20L * 1073741824;
    public bool SevereSpacePressure => TargetUsedPercent >= 90 || TargetFreeBytes < 20L * 1073741824;
    public string SmartModeColor => SevereSpacePressure ? "#B91C1C" : "#92400E";
    public string BlockingWarning { get; private set; } = "";
    public bool HasBlockingWarning => !string.IsNullOrEmpty(BlockingWarning);
    public string SmartModeNotice => SmartModeActive ? $"{(TargetUsedPercent is double used ? $"Volume {used:F1}% full" : "Low available space")}. Sustained capped at 5s / 16 GiB. Low space can reduce write speed; free space below 80% used may improve consistency." : "";
    public string WriteLimitSummary
    {
        get
        {
            try { var config = Configuration(); return $"Temporary file: {config.RequiredFileBytes / 1073741824d:F2} GiB | maximum total writes: {config.MaximumWriteBytes() / 1073741824d:F2} GiB. Writes reuse the file; they are not occupied disk space."; }
            catch (Exception ex) when (ex is OverflowException or InvalidOperationException) { return "Review workload settings before starting."; }
        }
    }
    public string WorkloadDescription => $"{BenchmarkMode} | {Iterations} pass(es) | " + (TimedMode ? $"{DurationSeconds}s per profile, not total time" : "Fixed workload; finishes after the selected bytes/operations") + (SustainedWrite ? $" | sustained up to {SustainedDurationSeconds}s" : " | sustained off");
    public string SessionProblem => DescribeSessionProblem(result);
    public bool HasSessionProblem => !string.IsNullOrWhiteSpace(SessionProblem);
    internal static string DescribeSessionProblem(BenchmarkSession? session) => session is null || (session.Completion == BenchmarkCompletion.Completed && session.CleanupSucceeded) ? "" :
        $"{session.Completion}: {session.Reason ?? "No additional error was recorded."}\n" + (session.CleanupSucceeded ? "Temporary benchmark file was cleaned. Reported writes are cumulative I/O, not space still occupied." : $"Cleanup needs attention. Owned file: {session.BenchmarkFilePath ?? "Path unavailable"}. Use Retry owned-file cleanup; do not delete unrelated files.");
    public double TbwWrittenPercent => Tbw is { WrittenTB: double written, RemainingPercent: not null } value && value.RatedTB > 0 ? Math.Clamp(written / value.RatedTB * 100, 0, 100) : 0;
    public string TbwScaleDisplay => Tbw?.WrittenTB is double written ? $"{written:F3} / {Tbw.RatedTB:F0} TB {(Tbw.RatingVerified ? "manufacturer endurance" : "unverified reference")}" : Tbw is { RatedTB: > 0 } unavailable ? $"Host writes unavailable; {unavailable.RatedTB:F0} TBW estimated reference" : "Diagnostics not yet queried - Run Diagnostics Now";
    public string TemperatureValue => Thermal?.ObservedC is double c ? $"{c:F1} °C" : "Not observed";
    public string TemperatureObservation => Thermal?.ObservedAt is { } observed ? $"{Thermal.State} | Observed {observed.ToLocalTime():g}" : "Run Diagnostics to observe temperature";
    public bool HasBenchmarkResult => result is not null;
    internal static string LinkBadge(pixinit.Core.History.SnapshotDevice? device) => device?.Protocol.Equals("Nvme", StringComparison.OrdinalIgnoreCase) == true
        ? device.PcieGeneration is int gen && device.PcieLaneWidth is int width ? $"NVMe Gen{gen} x{width}" : "NVMe - link not reported"
        : device?.Protocol.Equals("Sata", StringComparison.OrdinalIgnoreCase) == true ? "SATA - 600 MB/s interface ceiling" : "Filesystem benchmark";
    public string ResultBadge => result is null ? "Ready for a new benchmark" : result.Completion != BenchmarkCompletion.Completed ? $"{LinkBadge(result.Target.Device)} | {result.Completion} - see session details below" : $"{LinkBadge(result.Target.Device)} | {SequentialReadCard.Best} read | {SequentialReadCard.Average} average | {result.Completion}" + (result.SustainedOverallMBs is double speed ? $" | Sustained {speed:F1} MB/s" : "");
    public string PerformanceColor => result?.ValidationWarning.StartsWith("INVALID", StringComparison.Ordinal) == true ? "#B91C1C" : "#475569";
    public string PerformanceNotice => result is null ? "Performance results will appear after the test." : result.ValidationWarning.StartsWith("INVALID", StringComparison.Ordinal) || !result.Target.PhysicalIdentityReliable || result.Target.Device?.Bus.Contains("Usb", StringComparison.OrdinalIgnoreCase) == true ? result.ValidationWarning : "Measured performance. Free space, temperature and background activity can affect speed.";
    public sealed record ProfileCardRow(BenchmarkProfileSummary Summary, string Tooltip);
    public IReadOnlyList<ProfileCardRow> ProfileCards => result?.ProfileSummaries.Select(s => new ProfileCardRow(s, string.Join("\n", result.PassResults.Where(p => p.Profile == s.Profile && p.Measurement.Operation == s.Operation).Select(p => p.Display)))).ToArray() ?? [];
    public ResultCard SequentialReadCard => Card(BenchmarkOperation.SequentialRead);
    public ResultCard SequentialWriteCard => Card(BenchmarkOperation.SequentialWrite);
    public ResultCard RandomReadCard => Card(BenchmarkOperation.RandomRead);
    public ResultCard RandomWriteCard => Card(BenchmarkOperation.RandomWrite);
    private ResultCard Card(BenchmarkOperation operation)
    {
        var profile = BenchmarkProfile.All.FirstOrDefault(p => (SelectedProfiles & p.Id) != 0 && p.Random == (operation is BenchmarkOperation.RandomRead or BenchmarkOperation.RandomWrite));
        var summary = result?.ProfileSummaries.FirstOrDefault(s => s.Profile == profile?.Name && s.Operation == operation);
        if (summary is null)
        {
            var attempted = result?.PassResults.FirstOrDefault(p => p.Profile == profile?.Name && p.Measurement.Operation == operation);
            return new(attempted is null ? "Not run" : "Incomplete", "No completed average", profile?.Name ?? "No selected profile", attempted?.Display + "\n" + attempted?.Measurement.Error ?? "No completed passes");
        }
        var passes = result!.PassResults.Where(p => p.Profile == summary.Profile && p.Measurement.Operation == operation).ToArray();
        var display = WritePassPresentation.Create(passes);
        return new(display.FilteredAverage is double filtered ? $"Filtered avg {filtered:F2} MB/s ({display.Retained}/{display.Total} passes)" : $"{summary.AverageMBs:F2} MB/s",
            $"Best {summary.BestMBs:F2} MB/s | {summary.CompletedPasses} passes" + (profile!.Random ? $" | {summary.AverageIOPS:F0} IOPS" : ""), summary.Profile, display.Tooltip + (result.TargetVolumeUsedPercentAtStart is double used ? $"\nVolume at start: {used:F1}% used; cache exhaustion is not confirmed." : ""))
        { MeasuredAverage = display.FilteredAverage.HasValue ? $"All-pass avg {summary.AverageMBs:F2} MB/s | {display.Total - display.Retained} low-write outlier(s) filtered" : "" };
    }
    public string WriteDropWarning
    {
        get
        {
            if (result is null) return "";
            var slow = result.PassResults.Where(p => p.IncludeInAverage && p.Measurement.State == BenchmarkOperationState.Completed &&
                p.Measurement.Operation is BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite && p.Measurement.MegabytesPerSecond < 100).ToArray();
            if (slow.Length == 0) return "";
            return (result.TargetVolumeUsedPercentAtStart is double used ? $"Volume at start: {used:F1}% used. " : "") + $"{slow.Length} low-write pass(es): " + string.Join("; ", slow.Take(3).Select(p => $"{p.Profile} pass {p.Pass}: {p.Measurement.MegabytesPerSecond:F2} MB/s")) +
                (slow.Length > 3 ? "; more in pass tooltips" : "") + ". Filtered averages are labeled separately; all measured results are retained. Low space may affect speed; cache exhaustion or a defect is not confirmed.";
        }
    }
    public bool HasWriteDropWarning => !string.IsNullOrEmpty(WriteDropWarning);
    private void InitializePresentation()
    {
        QuickModeCommand = new(() => SetTestMode(false), () => ConfigurationEditable);
        DeepModeCommand = new(() => SetTestMode(true), () => ConfigurationEditable);
        CustomModeCommand = new(() => { benchmarkMode = "Custom"; AdvancedOpen = true; Changed(nameof(BenchmarkMode)); Changed(nameof(AdvancedOpen)); }, () => ConfigurationEditable);
        Size512Command = new(() => FileSizeMiB = 512, () => ConfigurationEditable);
        Size1024Command = new(() => FileSizeMiB = 1024, () => ConfigurationEditable);
        Size2048Command = new(() => FileSizeMiB = 2048, () => ConfigurationEditable);
    }
    private void SetTestMode(bool deep)
    {
        benchmarkMode = deep ? "Deep" : "Quick";
        UseUnbuffered = true; TimedMode = deep; Iterations = deep ? 3 : 1; DurationSeconds = 5; FileSizeMiB = deep ? 1024 : 512; RandomOperations = deep ? 65536 : 1000;
        SelectedProfiles = BenchmarkProfiles.All; IncludeWrites = true; IncludeRandom = true;
        SustainedWrite = deep; SustainedBudgetMiB = 16384; SustainedDurationSeconds = SmartDuration(deep ? 30 : 15, TargetUsedPercent, TargetFreeBytes);
        AdvancedOpen = false; Changed(nameof(BenchmarkMode)); Changed(nameof(AdvancedOpen)); NotifyPresentation();
    }
    internal static void ValidateBenchmarkLocation(string directory)
    {
        string full = Path.GetFullPath(directory);
        bool knownCloudRoot = new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable).Where(root => !string.IsNullOrWhiteSpace(root))
            .Any(root => (full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(Path.GetFullPath(root!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        bool namedCloudFolder = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("OneDrive", StringComparison.OrdinalIgnoreCase) || part.StartsWith("OneDrive - ", StringComparison.OrdinalIgnoreCase));
        if (knownCloudRoot || namedCloudFolder) throw new InvalidOperationException("Choose a local folder outside OneDrive for benchmark files. Syncing can retain copies or interfere with cleanup. The suggested PIXINIT_TEST folder is local.");
    }
    internal static int SmartDuration(int requested, double? usedPercent, long? freeBytes = null) => usedPercent > 80 || freeBytes < 20L * 1073741824 ? Math.Min(5, requested) : requested;
    private async Task<bool> PreflightAsync()
    {
        using var source = new CancellationTokenSource();
        cancellation = source; preflighting = true; StartCommand.Refresh(); CancelCommand.Refresh(); NotifyPresentation();
        try
        {
            await RefreshTargetSpaceAsync();
            if (!source.IsCancellationRequested) return true;
            Status = "Benchmark not started - diagnostic preparation cancelled"; Changed(nameof(Status));
            return false;
        }
        finally { cancellation = null; preflighting = false; StartCommand.Refresh(); CancelCommand.Refresh(); NotifyPresentation(); }
    }
    private async Task RefreshTargetSpaceAsync()
    {
        string path = TargetDirectory; long version = ++targetSpaceGeneration;
        var capacity = await Task.Run(() =>
        {
            try { var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!); long total = drive.TotalSize, free = drive.AvailableFreeSpace; return ((long?)free, total > 0 ? (double?)(100d * (total - free) / total) : null); }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return ((long?)null, (double?)null); }
        });
        if (version != targetSpaceGeneration || path != TargetDirectory) return;
        TargetFreeBytes = capacity.Item1; TargetUsedPercent = capacity.Item2; NotifyPresentation();
    }
    internal static void ValidateStartSpace(long available)
    {
        if (available <= 10L * 1073741824) throw new InvalidOperationException("Free up space: more than 10 GiB available is required before starting. Low free space can reduce performance.");
    }
    private void NotifyPresentation()
    {
        foreach (var name in new[] { nameof(BlockingWarning), nameof(HasBlockingWarning), nameof(SevereSpacePressure), nameof(SmartModeColor), nameof(ConfigurationEditable), nameof(TargetSpaceDisplay), nameof(TargetSpaceColor), nameof(SmartModeNotice), nameof(SmartModeActive), nameof(WriteLimitSummary), nameof(WorkloadDescription), nameof(SessionProblem), nameof(HasSessionProblem), nameof(TbwWrittenPercent), nameof(TbwScaleDisplay), nameof(TemperatureValue), nameof(TemperatureObservation), nameof(ResultBadge), nameof(HasBenchmarkResult), nameof(PerformanceNotice), nameof(PerformanceColor), nameof(ProfileCards), nameof(WriteDropWarning), nameof(HasWriteDropWarning), nameof(SequentialReadCard), nameof(SequentialWriteCard), nameof(RandomReadCard), nameof(RandomWriteCard), nameof(RunProgressDisplay) }) Changed(name);
        QuickModeCommand?.Refresh(); DeepModeCommand?.Refresh(); CustomModeCommand?.Refresh(); Size512Command?.Refresh(); Size1024Command?.Refresh(); Size2048Command?.Refresh();
    }
    private long progressStarted;
    private readonly Dictionary<string, long> reportedWrites = new();
    private double sessionElapsed;
    private long reportedWriteBytes;
    public string RunProgressDisplay => progressStarted == 0 ? "Not started" : $"{OverallProgress:F0}% | {sessionElapsed:F1}s elapsed | {(Busy && OverallProgress > 2 && OverallProgress < 100 ? $"~{sessionElapsed * (100 - OverallProgress) / OverallProgress:F0}s remaining (estimate)" : Busy ? "Estimating remaining time" : "Finished")} | {reportedWriteBytes / 1073741824d:F2} GiB application writes";
    private void TrackSessionProgress(BenchmarkProgress p)
    {
        sessionElapsed = Stopwatch.GetElapsedTime(progressStarted).TotalSeconds;
        if (p.Operation is null or BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite or BenchmarkOperation.SustainedWrite)
        {
            string key = p.Operation is null ? "Preparation" : $"{p.Pass}:{p.Operation}:{(p.Operation == BenchmarkOperation.SustainedWrite ? "Sustained" : p.Stage)}";
            reportedWrites[key] = Math.Max(reportedWrites.GetValueOrDefault(key), p.BytesProcessed);
            reportedWriteBytes = reportedWrites.Values.Sum();
        }
        Changed(nameof(RunProgressDisplay));
    }
}
