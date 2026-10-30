using System.IO;
using pixinit.Core.Benchmark;
using pixinit.Core.Devices;
using pixinit.ViewModels.Shared;

namespace pixinit.ViewModels.Benchmark;

public sealed partial class BenchmarkViewModel
{
    public sealed record BenchmarkCell(string Value, string Detail, string Tooltip);
    public sealed record BenchmarkRow(string Name, string Description, ActionCommand RunCommand, BenchmarkCell Read, BenchmarkCell Write);
    public ActionCommand RunAllCommand { get; private set; } = null!;
    public ActionCommand MainBenchmarkCommand => Busy || PreparingDiagnostics || preflighting ? CancelCommand : RunAllCommand;
    public string MainBenchmarkLabel => Busy || PreparingDiagnostics || preflighting ? "Stop" : "All";
    public IReadOnlyList<string> BenchmarkVolumes { get; private set; } = [];
    private string? selectedBenchmarkVolume;
    public string? SelectedBenchmarkVolume
    {
        get => selectedBenchmarkVolume;
        set
        {
            if (value == selectedBenchmarkVolume) return;
            selectedBenchmarkVolume = value;
            SetVolumeTarget(value);
            Changed();
        }
    }
    private BenchmarkProfiles automaticProfiles = BenchmarkProfiles.Standard;
    private IReadOnlyList<BenchmarkProfile> automaticRows = BenchmarkProfile.ForDevice(false);
    private readonly Dictionary<BenchmarkProfiles, ActionCommand> rowCommands = new();
    private readonly List<BenchmarkPassResult> livePasses = new();
    public string AutomaticProfileLabel => automaticProfiles == BenchmarkProfiles.Nvme ? "NVMe profile" : "Standard SATA / storage profile";
    public string SimpleWorkloadNotice => $"{AutomaticProfileLabel} | 5 seconds per read/write test | Average of completed valid passes";
    public string SimpleSpaceNotice => SmartModeActive ? "Low free space can reduce write speed. Freeing space may improve consistency." : "";
    public string LastRunDescription => result is null ? "Choose a volume and select All to start." :
        $"Last run: {result.Configuration.Iterations} pass(es), {result.Configuration.FileSizeBytes / 1048576:N0} MiB, {result.Target.VolumeRoot} | {result.Completion}";
    public string SimpleWriteNotice => WriteLimitSummary + " Hover over a result for Best, IOPS and pass details.";
    public IReadOnlyList<BenchmarkRow> BenchmarkRows => automaticRows.Select(profile => new BenchmarkRow(
        profile.Name, profile.Random ? "Small files / applications" : "Large files",
        RowCommand(profile.Id), CurrentCell(profile.Name, profile.Random ? BenchmarkOperation.RandomRead : BenchmarkOperation.SequentialRead),
        CurrentCell(profile.Name, profile.Random ? BenchmarkOperation.RandomWrite : BenchmarkOperation.SequentialWrite))).ToArray();

    private BenchmarkCell CurrentCell(string profile, BenchmarkOperation operation) => Busy && result is null
        ? CellFromPasses(livePasses, Iterations, profile, operation, true) : Cell(result, profile, operation);

    internal static BenchmarkCell Cell(BenchmarkSession? session, string profile, BenchmarkOperation operation) =>
        CellFromPasses(session?.PassResults ?? [], session?.Configuration.Iterations ?? 0, profile, operation, false);

    internal static BenchmarkCell CellFromPasses(IReadOnlyList<BenchmarkPassResult> source, int totalPasses, string profile, BenchmarkOperation operation, bool running)
    {
        var passes = source.Where(p => p.Profile == profile && p.Measurement.Operation == operation).ToArray();
        var summary = DirectBenchmarkPolicy.Summarize(passes).SingleOrDefault();
        string tooltip = string.Join("\n", passes.Select(p => p.Display + (p.Measurement.Error is { } error ? " | " + error : "")));
        if (summary is null) return new(passes.Length == 0 ? "--" : "Incomplete", "MB/s", passes.Length == 0 ? "Not measured in the last run" : tooltip);
        double? latency = passes.Where(p => p.IncludeInAverage && p.Measurement.State == BenchmarkOperationState.Completed)
            .Select(p => p.Measurement.Latency?.AverageMilliseconds).Where(v => v.HasValue).Average();
        tooltip = $"Average {summary.AverageMBs:F2} MB/s | Best {summary.BestMBs:F2} MB/s\n" +
            $"{summary.AverageIOPS:N0} IOPS" + (latency is double ms ? $" | Mean completion latency {ms:F3} ms" : "") + "\n" + tooltip;
        return new(summary.AverageMBs.ToString("F2", System.Globalization.CultureInfo.CurrentCulture),
            $"MB/s | {summary.CompletedPasses}/{totalPasses} passes{(running ? " | Running" : "")}", tooltip);
    }
    private ActionCommand RowCommand(BenchmarkProfiles profile)
    {
        if (!rowCommands.TryGetValue(profile, out var command))
            rowCommands[profile] = command = new(async () => await RunSimpleAsync(profile), () => ConfigurationEditable && StartCommand.CanExecute(null));
        return command;
    }
    private void InitializeSimpleBenchmark()
    {
        RunAllCommand = new(async () => await RunSimpleAsync(automaticProfiles), () => ConfigurationEditable && StartCommand.CanExecute(null));
        UseUnbuffered = true; TimedMode = true; DurationSeconds = 5; Iterations = 3; FileSizeMiB = 1024;
        SelectedProfiles = automaticProfiles; IncludeWrites = true; IncludeRandom = true; SustainedWrite = false;
    }
    internal async Task RunSimpleAsync(BenchmarkProfiles profiles)
    {
        if (!ConfigurationEditable || !StartCommand.CanExecute(null)) return;
        UseUnbufferedForSimpleRun(profiles);
        await RunFromUiAsync();
    }
    private void UseUnbufferedForSimpleRun(BenchmarkProfiles profiles)
    {
        useUnbuffered = true; timedMode = true; durationSeconds = 5;
        SequentialRead = SequentialWrite = RandomRead = RandomWrite = true;
        sustainedWrite = false; SelectedProfiles = profiles;
        ConfigurationChanged();
    }
    private void SetSimpleDevice(StorageDevice? device)
    {
        bool nvme = device?.Protocol == StorageProtocol.Nvme;
        automaticProfiles = nvme ? BenchmarkProfiles.Nvme : BenchmarkProfiles.Standard;
        automaticRows = BenchmarkProfile.ForDevice(nvme);
        BenchmarkVolumes = device?.MountPoints.Where(p => Path.IsPathRooted(p) &&
            string.Equals(Path.GetFullPath(p), Path.GetPathRoot(p), StringComparison.OrdinalIgnoreCase) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        selectedBenchmarkVolume = BenchmarkVolumes.FirstOrDefault();
        SelectedProfiles = automaticProfiles;
        Changed(nameof(BenchmarkVolumes)); Changed(nameof(SelectedBenchmarkVolume));
        SetVolumeTarget(selectedBenchmarkVolume);
        Status = device is null ? "Select a physical disk above." : BenchmarkVolumes.Count == 0 ? "No mounted volume available for this disk." : "Ready - select All to test this volume.";
        Changed(nameof(Status));
    }
    private void SetVolumeTarget(string? root)
    {
        if (root is null) { suggestedDirectory = null; TargetDirectory = ""; return; }
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        suggestedDirectory = Path.Combine(string.Equals(Path.GetPathRoot(local), root, StringComparison.OrdinalIgnoreCase) ? local : root, "PIXINIT_TEST");
        TargetDirectory = suggestedDirectory;
    }
    private void NotifySimpleBenchmark()
    {
        foreach (string name in new[] { nameof(BenchmarkRows), nameof(AutomaticProfileLabel), nameof(SimpleWorkloadNotice), nameof(SimpleSpaceNotice), nameof(LastRunDescription), nameof(SimpleWriteNotice), nameof(MainBenchmarkCommand), nameof(MainBenchmarkLabel) }) Changed(name);
        RunAllCommand?.Refresh();
        foreach (var command in rowCommands.Values) command.Refresh();
    }
}
