using System.IO;
using pixinit.Core.Benchmark;

namespace pixinit.ViewModels.Benchmark;
public sealed partial class BenchmarkViewModel
{
    private bool timedMode = true;
    private int durationSeconds = 5;
    public bool TimedMode { get => timedMode; set { timedMode = value; ConfigurationChanged(); Changed(); Changed(nameof(PassesExplanation)); } }
    public int DurationSeconds { get => durationSeconds; set { durationSeconds = value; ConfigurationChanged(); Changed(); Changed(nameof(PassesExplanation)); } }
    public IReadOnlyList<int> TimedDurations => [5, 10, 20];
    public IReadOnlyList<BenchmarkPassResult> PassDetails => result?.PassResults ?? [];
    private bool useUnbuffered = true;
    public bool UseUnbuffered
    {
        get => useUnbuffered;
        set
        {
            useUnbuffered = value; fileSizeMiB = value ? 1024 : 32; iterations = value ? 3 : 1; if (!value) sustainedWrite = false; RandomOperations = value ? 65536 : 1000;
            ConfigurationChanged(); Changed(); Changed(nameof(FileSizes)); Changed(nameof(TestPasses)); Changed(nameof(FileSizeMiB)); Changed(nameof(Iterations)); Changed(nameof(ModeLabel)); Changed(nameof(PassesExplanation));
        }
    }
    private BenchmarkProfiles selectedProfiles = BenchmarkProfiles.All;
    public BenchmarkProfiles SelectedProfiles { get => selectedProfiles; set { selectedProfiles = value; ConfigurationChanged(); Changed(); NotifyProfileCards(); } }
    private void NotifyProfileCards()
    {
        NotifyPresentation();
        Changed(nameof(SequentialReadResult)); Changed(nameof(SequentialWriteResult));
        Changed(nameof(RandomReadProfileResult)); Changed(nameof(RandomWriteProfileResult));
    }
    public string RandomReadProfileResult => ProfileCard(BenchmarkOperation.RandomRead);
    public string RandomWriteProfileResult => ProfileCard(BenchmarkOperation.RandomWrite);
    private string ProfileCard(BenchmarkOperation operation)
    {
        if (result is null) return "Not run";
        if (!result.Configuration.Unbuffered) return operation == BenchmarkOperation.RandomRead ? $"{RndReadIOPS:F1} IOPS\n{RndReadMBs:F2} MiB/s" : $"{RndWriteIOPS:F1} IOPS\n{RndWriteMBs:F2} MiB/s";
        var selected = BenchmarkProfile.All.Where(p => (selectedProfiles & p.Id) != 0 && p.Random).ToArray();
        if (selected.Length == 0) return "Select a random profile above";
        var lines = selected.Select(profile =>
        {
            var summary = result.ProfileSummaries.FirstOrDefault(s => s.Profile == profile.Name && s.Operation == operation);
            if (summary is null)
            {
                bool invalid = result.PassResults.Any(r => r.Profile == profile.Name && r.Measurement.Operation == operation && !r.IncludeInAverage);
                return $"{profile.Name}: {(invalid ? "Invalid for device attribution" : "No completed average")}";
            }
            var passes = result.PassResults.Where(r => r.Profile == profile.Name && r.Measurement.Operation == operation && r.IncludeInAverage && r.Measurement.Iops.HasValue).ToArray();
            double bestIops = passes.Length == 0 ? 0 : passes.Max(r => r.Measurement.Iops!.Value);
            double averageMiB = summary.AverageIOPS * 4096 / 1048576d;
            double bestMiB = bestIops * 4096 / 1048576d;
            return $"{profile.Name} · Avg {summary.AverageIOPS:F1} IOPS / {averageMiB:F2} MiB/s · Best {bestIops:F1} IOPS / {bestMiB:F2} MiB/s ({summary.CompletedPasses} passes)";
        });
        return string.Join("\n", lines);
    }
    private string SequentialProfileCard(BenchmarkOperation operation)
    {
        if (result is null) return "Not run";
        if (!result.Configuration.Unbuffered) return BaseSequentialResult(operation);
        var selected = BenchmarkProfile.All.Where(p => (selectedProfiles & p.Id) != 0 && !p.Random).ToArray();
        if (selected.Length == 0) return "Select a sequential profile above";
        return string.Join("\n", selected.Select(profile =>
        {
            var summary = result.ProfileSummaries.FirstOrDefault(s => s.Profile == profile.Name && s.Operation == operation);
            if (summary is not null) return $"{profile.Name} · Avg {summary.AverageMBs:F2} MB/s · Best {summary.BestMBs:F2} MB/s ({summary.CompletedPasses} passes)";
            bool invalid = result.PassResults.Any(r => r.Profile == profile.Name && r.Measurement.Operation == operation && !r.IncludeInAverage);
            return $"{profile.Name}: {(invalid ? "Invalid for device attribution" : "No completed average")}";
        }));
    }
    private string BaseSequentialResult(BenchmarkOperation operation) { var r = result?.Results.FirstOrDefault(x => x.Operation == operation); return r is null ? "Not run" : r.State != BenchmarkOperationState.Completed ? $"{r.State} · incomplete" : $"{r.MegabytesPerSecond:F1} MB/s · {r.MebibytesPerSecond:F1} MiB/s"; }
    private void SelectProfile(BenchmarkProfiles profile, bool enabled) { SelectedProfiles = enabled ? selectedProfiles | profile : selectedProfiles & ~profile; }
    public bool SeqQ8 { get => (selectedProfiles & BenchmarkProfiles.Seq1MQ8T1) != 0; set { SelectProfile(BenchmarkProfiles.Seq1MQ8T1, value); Changed(); } }
    public bool SeqQ1 { get => (selectedProfiles & BenchmarkProfiles.Seq1MQ1T1) != 0; set { SelectProfile(BenchmarkProfiles.Seq1MQ1T1, value); Changed(); } }
    public bool RandomQ32 { get => (selectedProfiles & BenchmarkProfiles.Rnd4KQ32T16) != 0; set { SelectProfile(BenchmarkProfiles.Rnd4KQ32T16, value); Changed(); } }
    public bool RandomQ1 { get => (selectedProfiles & BenchmarkProfiles.Rnd4KQ1T1) != 0; set { SelectProfile(BenchmarkProfiles.Rnd4KQ1T1, value); Changed(); } }
    public IReadOnlyList<int> TestPasses => UseUnbuffered ? [1, 3, 5, 9] : [1, 2, 3, 4];
    public string PassesExplanation => $"{Iterations} = {Iterations} test pass(es). Average and best are separate; {(TimedMode ? $"{DurationSeconds}s submission window per test; circular file reuse if data exceeds file size." : "Fixed workload; writes are bounded by the selected file size and operation count.")} Two-second pause does not guarantee SLC recovery.";
    public string ModeLabel => (result?.Configuration.Unbuffered ?? UseUnbuffered) ? "Unbuffered file benchmark | OS cache bypass with write-through; controller caches may remain." : BenchmarkLabel;
    public string TargetNotice => string.IsNullOrWhiteSpace(TargetDirectory) ? "Select a drive or browse to a mapped volume." : string.Equals(Path.GetPathRoot(TargetDirectory), Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase) ? "OS volume: background activity can affect speed. A different folder does not remove that activity." : "Temporary owned test file on the selected volume.";
    public string ValidationNotice => result?.ValidationWarning ?? "Values are not capped or adjusted to expected specifications. Link speed and model-specific limits are not inferred.";
    public IReadOnlyList<BenchmarkProfileSummary> ProfileResults => result?.ProfileSummaries ?? [];
    public double OverallProgress { get; private set; }
    public string LiveSpeed { get; private set; } = "Current pass: not measured";
    public string AverageSpeed { get; private set; } = "Completed-pass average: not measured";
    private void ResetDirectProgress(bool starting = false)
    {
        progressStarted = starting ? System.Diagnostics.Stopwatch.GetTimestamp() : 0; sessionElapsed = 0; reportedWriteBytes = 0; reportedWrites.Clear();
        OverallProgress = 0; LiveSpeed = "Current pass: preparing"; AverageSpeed = "Completed-pass average: pending";
        Changed(nameof(OverallProgress)); Changed(nameof(LiveSpeed)); Changed(nameof(AverageSpeed));
    }
    private void UpdateDirectProgress(BenchmarkProgress p)
    {
        OverallProgress = p.Operation == BenchmarkOperation.SustainedWrite ? 90 + p.Percent / 10 : p.OverallPercent * (SustainedWrite ? 0.9 : 1);
        TrackSessionProgress(p);
        LiveSpeed = p.CurrentMBs is double speed ? $"Measuring: {p.ElapsedSeconds:F1}s / {(p.DurationSeconds > 0 ? p.DurationSeconds.ToString("F1") : "fixed workload")} - {speed:F2} MB/s current" : "Current pass: preparing";
        AverageSpeed = p.CompletedAverageMBs is double average ? $"Prior completed-pass average: {average:F2} MB/s" : "Completed-pass average: pending";
        Changed(nameof(OverallProgress)); Changed(nameof(LiveSpeed)); Changed(nameof(AverageSpeed));
    }
}