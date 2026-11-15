using System.Diagnostics;
using System.IO;
using pixinit.Core.Benchmark;
using pixinit.Core.Diagnostics.Common;
using pixinit.Infrastructure.Logging;
using pixinit.ViewModels.Shared;

namespace pixinit.ViewModels.Benchmark;

public sealed partial class BenchmarkViewModel
{
    public sealed record ResultCard(string Average, string Best, string Profile, string PassTooltip)
    {
        public string MeasuredAverage { get; init; } = "";
        public string AverageNumber => Average.EndsWith(" MB/s", StringComparison.Ordinal) ? Average[..^5] : Average;
        public string AverageUnit => Average.EndsWith(" MB/s", StringComparison.Ordinal) ? " MB/s" : "";
    }
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
    public string TargetDirectoryCompact
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TargetDirectory)) return "Choose a folder";
            if (TargetDirectory.Length <= 32) return TargetDirectory;
            string root = Path.GetPathRoot(TargetDirectory) ?? "";
            string leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(TargetDirectory));
            return string.IsNullOrEmpty(leaf) ? TargetDirectory : $"{root}...\\{leaf}";
        }
    }
    private bool advancedOpen;
    public bool AdvancedOpen { get => advancedOpen; set { if (advancedOpen == value) return; advancedOpen = value; Changed(); } }
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
    public string WorkloadDescription => $"{Iterations} pass(es) | " + (TimedMode ? $"{DurationSeconds}s per profile, not total time" : "Fixed workload; finishes after the selected bytes/operations") + (SustainedWrite ? $" | sustained up to {SustainedDurationSeconds}s" : " | sustained off");
    public IReadOnlyList<string> TargetVolumes => selectedDevice()?.MountPoints.Where(p => Path.IsPathRooted(p) && string.Equals(Path.GetFullPath(p), Path.GetPathRoot(p), StringComparison.OrdinalIgnoreCase) && Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    public string? SelectedTargetVolume
    {
        get => TargetVolumes.FirstOrDefault(root => string.Equals(root, Path.GetPathRoot(TargetDirectory), StringComparison.OrdinalIgnoreCase));
        set { if (!string.IsNullOrWhiteSpace(value) && TargetVolumes.Contains(value, StringComparer.OrdinalIgnoreCase)) { suggestedDirectory = Path.Combine(value, "PIXINIT_TEST"); TargetDirectory = suggestedDirectory; } }
    }
    public string OsDriveNotice => string.Equals(SelectedTargetVolume, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase) ? "OS drive may be slower" : "";
    public bool HasCompletedBenchmark => result?.Completion == BenchmarkCompletion.Completed;
    public bool ShowEmptyResults => !HasCompletedBenchmark;
    public bool HasSustainedResult => HasCompletedBenchmark && result?.SustainedOverallMBs is not null;
    public sealed record RepeatChoice(int Count, string Label);
    public IReadOnlyList<RepeatChoice> RepeatChoices { get; } = [new(1, "1 test"), new(3, "3 tests - Average"), new(5, "5 tests - More samples")];
    public RepeatChoice? SelectedRepeatChoice
    {
        get => RepeatChoices.FirstOrDefault(option => option.Count == Iterations);
        set { if (value is not null) Iterations = value.Count; }
    }
    public bool HasVerifiedEnduranceRating => Tbw is { RatingVerified: true, InterpretationVerified: true, RatedTB: > 0, RemainingPercent: not null };
    public string RandomReadIopsDisplay => result?.ProfileSummaries.FirstOrDefault(summary => summary.Operation == BenchmarkOperation.RandomRead)?.AverageIOPS is double iops ? $"{iops:N0} IOPS" : "";
    public string RandomWriteIopsDisplay => result?.ProfileSummaries.FirstOrDefault(summary => summary.Operation == BenchmarkOperation.RandomWrite)?.AverageIOPS is double iops ? $"{iops:N0} IOPS" : "";
    public string CompletedBanner => result is { Completion: BenchmarkCompletion.Completed } session
        ? $"✓ Test Completed - {SequentialReadCard.Average} Read | {SequentialWriteCard.Average} Write | {session.TotalSeconds:F1}s | {session.Target.VolumeRoot.TrimEnd(Path.DirectorySeparatorChar)} drive" : "";
    public string CompletedSpeedLine => result is { Completion: BenchmarkCompletion.Completed }
        ? $"Read {SequentialReadCard.Average}  |  Write {SequentialWriteCard.Average}" : "";
    public string CompletedMetaLine => result is { Completion: BenchmarkCompletion.Completed } session
        ? $"{session.TotalSeconds:F1}s  |  {session.Target.VolumeRoot.TrimEnd(Path.DirectorySeparatorChar)} drive" : "";
    public string CompletedProtocolBadge => result?.Target.Device is { } device ? LinkBadge(device) : "Measured file I/O";
    public double RemainingEndurancePercent => HasVerifiedEnduranceRating ? Math.Clamp(Tbw?.RemainingPercent ?? 0, 0, 100) : 0;
    public string UsbSpeedNotice => selectedDevice() is { Bus: Core.Devices.ConnectionBus.Usb } device
        ? device.UsbSpeed is { Length: > 0 } speed && speed.Contains("USB 2.0", StringComparison.Ordinal)
            ? speed + ". Speed limited by USB 2.0 port, use Blue USB 3.0 port for full speed"
            : device.UsbSpeed ?? "USB speed unavailable - hub port was not readable - Try a motherboard USB 3.0 port"
        : "";
    public bool HasUsbSpeedNotice => UsbSpeedNotice.Length > 0;
    public string ProtocolFooter => selectedDevice() is { } device ? $"Diagnostics: {device.Protocol}" + (device.Bus == Core.Devices.ConnectionBus.Usb ? " " + (device.UsbSpeed ?? "USB") : "") + (device.PcieGeneration is int gen && device.PcieLaneWidth is int lanes ? $" PCIe {gen}.0 x{lanes}" : "") : "";
    public string FriendlyHostWrites => Tbw?.WrittenTB is double written
        ? Tbw.RatingVerified && Tbw.InterpretationVerified && Tbw.RatedTB > 0 ? $"Host writes: {written:F1} TB of {Tbw.RatedTB:F0} TB rated endurance" : written < 1 ? $"Host writes: {written * 1e12 / 1073741824d:N2} GiB / {written * 1000:N2} GB" : $"Host writes: {written:F3} TB"
        : "Host writes unavailable - Remaining N/A";
    public string FriendlyEndurance => Tbw is { RatingVerified: true, InterpretationVerified: true, RemainingPercent: double remaining } ? $"{remaining:F0}% of rated endurance remaining (estimate)" :
        Tbw?.WrittenTB is null && Tbw is { RatedTB: > 0 } missing ? $"{missing.RatedTB:F0} TBW reference. Remaining N/A because host writes were not read" :
        Tbw is { RatingVerified: true, InterpretationVerified: false, RatedTB: > 0 } rated ? $"{rated.RatedTB:F0} TB manufacturer rating; SMART units unverified. Remaining N/A" :
        Tbw is { RatedTB: > 0 } unverified ? $"{unverified.RatedTB:F0} TBW estimated reference; device rating unavailable" : "Endurance rating unavailable";
    public string FriendlyTemperature => Thermal?.ObservedC is double c ? $"{c:F1} °C - {Thermal.State switch { ThermalState.Green => "Good", ThermalState.Yellow => "Warm", ThermalState.Red => "Hot", _ => "Observed" }}" : "Temperature unavailable";
    public string FriendlyTemperatureTime => Thermal?.ObservedAt is { } time ? $"Checked {time.ToLocalTime():g}" : "Run Diagnostics to check temperature";
    public string DetailDeviceName => selectedDevice()?.Model ?? "Selected drive";
    private BenchmarkConfiguration? DetailConfiguration
    {
        get
        {
            try { return result?.Configuration ?? Configuration(); }
            catch (Exception ex) when (ex is InvalidOperationException or OverflowException) { return null; }
        }
    }
    public string DetailFileSize => DetailConfiguration is { } config ? $"File: {config.FileSizeBytes / 1048576d:F0} MiB" : "File: unavailable";
    public string DetailPasses => DetailConfiguration is { } config ? $"Passes: {config.Iterations}" : "Passes: unavailable";
    public string DetailMaxWrites => DetailConfiguration is { } config ? $"Maximum writes: {config.MaximumWriteBytes() / 1073741824d:F2} GiB" : "Maximum writes: unavailable";
    public string DetailIoMode => DetailConfiguration is { } config ? $"I/O: {(config.Unbuffered ? "Unbuffered" : "Buffered")}" : "I/O: unavailable";
    private string DetailMetric(BenchmarkOperation operation, string label)
    {
        var summary = result?.ProfileSummaries.FirstOrDefault(value => value.Operation == operation);
        return summary is null ? $"{label}: Not measured" : $"{label}: {summary.AverageMBs:F1} avg / {summary.BestMBs:F1} best MB/s";
    }
    public string DetailSequentialRead => DetailMetric(BenchmarkOperation.SequentialRead, "Sequential read");
    public string DetailSequentialWrite => DetailMetric(BenchmarkOperation.SequentialWrite, "Sequential write");
    public string DetailRandomRead => DetailMetric(BenchmarkOperation.RandomRead, "Random read");
    public string DetailRandomWrite => DetailMetric(BenchmarkOperation.RandomWrite, "Random write");
    public string DetailTarget => $"Target: {result?.Target.Directory ?? TargetDirectory}";
    public string DetailTrim => $"TRIM: {result?.TrimStatus ?? "Not recorded"}";
    public string DetailCleanup => result is null ? "Cleanup: No test run" : result.CleanupSucceeded ? "Cleanup: Temporary file removed" : "Cleanup: Attention required";
    public string AdvancedRawLog
    {
        get
        {
            string discovery;
            try { discovery = File.Exists(DiscoveryLog.FilePath) ? File.ReadAllText(DiscoveryLog.FilePath) : "No discovery log available."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { discovery = "Discovery log could not be read."; }
            return $"Discovery log\n{discovery}\n\nBenchmark log\n{ResultDetails}";
        }
    }
    public string CompactSpaceNotice => TargetFreeBytes is long free
        ? $"Free: {free / 1073741824d:F1} GiB" + (TargetUsedPercent is double used ? $" | {used:F1}% used" : "") + (OsDriveNotice.Length > 0 ? " | OS drive may be slower" : "")
        : "Free space checked before test";
    public string AdvancedSummary
    {
        get
        {
            BenchmarkConfiguration config;
            try { config = Configuration(); }
            catch (Exception ex) when (ex is InvalidOperationException or OverflowException) { return ex.Message; }
            string metric(BenchmarkOperation operation)
            {
                var summary = result?.ProfileSummaries.FirstOrDefault(s => s.Operation == operation);
                return summary is null ? "Not measured" : $"{summary.AverageMBs:F1} avg / {summary.BestMBs:F1} best MB/s";
            }
            return $"{config.FileSizeBytes / 1048576d:F0} MiB file | {config.Iterations} pass(es) | {config.MaximumWriteBytes() / 1073741824d:F2} GiB max writes | {(config.Unbuffered ? "Unbuffered" : "Buffered")}\n" +
                $"Sequential read: {metric(BenchmarkOperation.SequentialRead)} | write: {metric(BenchmarkOperation.SequentialWrite)}\n" +
                $"Random read: {metric(BenchmarkOperation.RandomRead)} | write: {metric(BenchmarkOperation.RandomWrite)}\n" +
                $"Target: {TargetDirectory} | {ProtocolFooter}\n" +
                $"Temperature: {FriendlyTemperature} | TRIM: {result?.TrimStatus ?? "Not reported"}";
        }
    }
    public string AdvancedTechnical
    {
        get
        {
            BenchmarkConfiguration config;
            try { config = result?.Configuration ?? Configuration(); }
            catch (Exception ex) when (ex is InvalidOperationException or OverflowException) { return ex.Message; }
            return $"File: {config.FileSizeBytes:N0} bytes; sequential block: {config.SequentialBlockBytes:N0}; random block: {config.RandomBlockBytes:N0}; passes: {config.Iterations}; random operations: {config.RandomOperationCount}\n" +
                $"I/O: {(config.Unbuffered ? "Unbuffered with write-through" : "Buffered")}; OS cache bypass requested in unbuffered mode; controller cache may remain\n" +
                $"Preparation: {result?.PreparationBytesWritten ?? 0:N0} bytes / {result?.PreparationSeconds ?? 0:F3}s; cleanup: {result?.CleanupSeconds ?? 0:F3}s; total: {result?.TotalSeconds ?? 0:F3}s\n" +
                $"{WriteLimitSummary}\n{ValidationNotice}";
        }
    }
    public string ShortPassTooltip(BenchmarkOperation operation)
    {
        var pass = result?.PassResults.FirstOrDefault(p => p.Measurement.Operation == operation && p.Measurement.State == BenchmarkOperationState.Completed);
        return pass is null ? "No completed pass" : $"{pass.Measurement.MegabytesPerSecond:F0} MB/s | {pass.Measurement.MeasuredSeconds:F2}s | {(pass.IncludeInAverage && pass.IsValidForDeviceAttribution ? "Valid" : "Excluded")}";
    }
    public string SequentialReadTip => ShortPassTooltip(BenchmarkOperation.SequentialRead);
    public string SequentialWriteTip => ShortPassTooltip(BenchmarkOperation.SequentialWrite);
    public string RandomReadTip => ShortPassTooltip(BenchmarkOperation.RandomRead);
    public string RandomWriteTip => ShortPassTooltip(BenchmarkOperation.RandomWrite);
    public string SessionProblem => DescribeSessionProblem(result);
    public bool HasSessionProblem => !string.IsNullOrWhiteSpace(SessionProblem);
    internal static string DescribeSessionProblem(BenchmarkSession? session) => session is null || (session.Completion == BenchmarkCompletion.Completed && session.CleanupSucceeded) ? "" :
        $"{session.Completion}: {session.Reason ?? "No additional error was recorded."}\n" + (session.CleanupSucceeded ? "Temporary benchmark file was cleaned. Reported writes are cumulative I/O, not space still occupied." : $"Cleanup needs attention. Owned file: {session.BenchmarkFilePath ?? "Path unavailable"}. Use Retry owned-file cleanup; do not delete unrelated files.");
    public double TbwWrittenPercent => Tbw is { WrittenTB: double written, InterpretationVerified: true, RemainingPercent: not null } value && value.RatedTB > 0 ? Math.Clamp(written / value.RatedTB * 100, 0, 100) : 0;
    public string TbwScaleDisplay => Tbw is { InterpretationVerified: false } uncertain ? $"SMART host-write units unverified; {(uncertain.RatingVerified ? $"{uncertain.RatedTB:F0} TB manufacturer rating" : $"{uncertain.RatedTB:F0} TB reference")}" :
        Tbw?.WrittenTB is double written ? $"{written:F3} / {Tbw.RatedTB:F0} TB {(Tbw.RatingVerified ? "manufacturer endurance" : "unverified reference")}" :
        Tbw is { RatedTB: > 0 } unavailable ? $"Host writes unavailable; {unavailable.RatedTB:F0} TBW {(unavailable.RatingVerified ? "manufacturer rating" : "estimated reference")}" : "Diagnostics not yet queried - Run Diagnostics Now";
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
            return new(attempted is null ? "— MB/s" : "Incomplete", attempted is null ? "Ready to test" : "No completed average", profile?.Name ?? "No selected profile", attempted?.Display + "\n" + attempted?.Measurement.Error ?? "No completed passes");
        }
        var passes = result!.PassResults.Where(p => p.Profile == summary.Profile && p.Measurement.Operation == operation).ToArray();
        var display = WritePassPresentation.Create(passes);
        return new(display.FilteredAverage is double filtered ? $"{filtered:F2} MB/s" : $"{summary.AverageMBs:F2} MB/s",
            $"Best {summary.BestMBs:F2} MB/s | {(display.FilteredAverage.HasValue ? $"{display.Retained}/{display.Total}" : summary.CompletedPasses.ToString())} passes" + (profile!.Random ? $" | {summary.AverageIOPS:F0} IOPS" : ""), summary.Profile, display.Tooltip + (result.TargetVolumeUsedPercentAtStart is double used ? $"\nVolume at start: {used:F1}% used; cache exhaustion is not confirmed." : ""))
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
        if (UseUnbuffered && selectedDevice() is not null && TargetUsedPercent is > 80)
        {
            if (SustainedWrite) SustainedWrite = false;
            if (FileSizeMiB > 1024) FileSizeMiB = 1024;
        }
    }
    internal static void ValidateStartSpace(long available)
    {
        if (available <= 10L * 1073741824) throw new InvalidOperationException("Free up space: more than 10 GiB available is required before starting. Low free space can reduce performance.");
    }
    private void NotifyPresentation()
    {
        foreach (var name in new[] { nameof(BlockingWarning), nameof(HasBlockingWarning), nameof(SevereSpacePressure), nameof(SmartModeColor), nameof(ConfigurationEditable), nameof(TargetSpaceDisplay), nameof(TargetSpaceColor), nameof(CompactSpaceNotice), nameof(AdvancedSummary), nameof(AdvancedTechnical), nameof(SequentialReadTip), nameof(SequentialWriteTip), nameof(RandomReadTip), nameof(RandomWriteTip), nameof(SmartModeNotice), nameof(SmartModeActive), nameof(WriteLimitSummary), nameof(WorkloadDescription), nameof(SessionProblem), nameof(HasSessionProblem), nameof(TbwWrittenPercent), nameof(HasVerifiedEnduranceRating), nameof(RemainingEndurancePercent), nameof(RandomReadIopsDisplay), nameof(RandomWriteIopsDisplay), nameof(TbwScaleDisplay), nameof(TemperatureValue), nameof(TemperatureObservation), nameof(ResultBadge), nameof(HasBenchmarkResult), nameof(HasCompletedBenchmark), nameof(ShowEmptyResults), nameof(HasSustainedResult), nameof(CompletedBanner), nameof(CompletedSpeedLine), nameof(CompletedMetaLine), nameof(CompletedProtocolBadge), nameof(ProtocolFooter), nameof(FriendlyHostWrites), nameof(FriendlyEndurance), nameof(FriendlyTemperature), nameof(FriendlyTemperatureTime), nameof(PerformanceNotice), nameof(PerformanceColor), nameof(ProfileCards), nameof(WriteDropWarning), nameof(HasWriteDropWarning), nameof(SequentialReadCard), nameof(SequentialWriteCard), nameof(RandomReadCard), nameof(RandomWriteCard), nameof(RunProgressDisplay) }) Changed(name);
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
