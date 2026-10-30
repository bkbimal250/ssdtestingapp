using System.Diagnostics;
using System.IO;
using pixinit.Application.Benchmarking;
using pixinit.Core.Benchmark;
using pixinit.Core.Devices;
using pixinit.Infrastructure.Benchmarking;
using pixinit.ViewModels.Benchmark;

namespace pixinit.Tests;

internal static partial class Program
{
    private sealed class ReviewOnlyConsent : IWriteConsent
    {
        public BenchmarkConfiguration? Configuration { get; private set; }
        public Task<bool> RequestAsync(BenchmarkConfiguration configuration, long maximumWriteBytes)
        { Configuration = configuration; return Task.FromResult(false); }
    }
    private static async Task SimpleBenchmarkTests()
    {
        var sataProfiles = BenchmarkProfile.ForDevice(false);
        var nvmeProfiles = BenchmarkProfile.ForDevice(true);
        Check(sataProfiles.Select(p => p.Name).SequenceEqual(["SEQ1M Q8T1", "SEQ1M Q1T1", "RND4K Q32T1", "RND4K Q1T1"]), "Standard recipe matches the CDM8 block, queue and worker profile names");
        Check(nvmeProfiles.Select(p => p.Name).SequenceEqual(["SEQ1M Q8T1", "SEQ128K Q32T1", "RND4K Q32T16", "RND4K Q1T1"]), "NVMe recipe uses 128 KiB sequential Q32 and random Q32 per sixteen workers");
        Check(sataProfiles[2] is { BlockBytes: 4096, QueuePerWorker: 32, Workers: 1 } && nvmeProfiles[1] is { BlockBytes: 131072, QueuePerWorker: 32, Workers: 1 }, "Actual profile parameters match their displayed block and concurrency labels");
        var vm = new BenchmarkViewModel(new FileBenchmarkEngine(), () => [], () => null, () => { });
        var nvme = SyntheticNvme();
        vm.SetSuggestedTarget(nvme);
        Check(vm.Configuration().Profiles == BenchmarkProfiles.Nvme && vm.BenchmarkRows.Select(r => r.Name).SequenceEqual(nvmeProfiles.Select(p => p.Name)), "Selecting NVMe configures and displays the automatic NVMe recipe");
        vm.SetSuggestedTarget(nvme with { Protocol = StorageProtocol.Sata });
        Check(vm.Configuration().Profiles == BenchmarkProfiles.Standard && vm.BenchmarkRows.Count == 4, "Switching to SATA configures the standard recipe without extra user choices");
        string volume = Path.GetPathRoot(Path.GetTempPath())!;
        vm.SetSuggestedTarget(nvme with { MountPoints = [volume, volume] });
        Check(vm.BenchmarkVolumes.Count == 1 && vm.SelectedBenchmarkVolume == volume && vm.TargetDirectory.StartsWith(volume, StringComparison.OrdinalIgnoreCase), "Mapped-volume selector deduplicates roots and targets the selected volume");
        vm.SetSuggestedTarget(nvme with { MountPoints = [] });
        Check(vm.BenchmarkVolumes.Count == 0 && !vm.RunAllCommand.CanExecute(null), "No mounted volume disables All instead of silently benchmarking another drive");
        long cap = DirectBenchmarkPolicy.MaximumApplicationWrites;
        Check(DirectBenchmarkPolicy.RemainingTimedWriteBytes(1073741824) == cap - 1073741824 && DirectBenchmarkPolicy.RemainingTimedWriteBytes(cap) == 0 && DirectBenchmarkPolicy.RemainingTimedWriteBytes(cap + 1) == 0, "Shared timed budget subtracts measured application writes and never goes negative");
        Check(DirectBenchmarkPolicy.RemainingTimedWriteBytes(1073741824) > cap / 12, "Fast NVMe job can use remaining shared allowance rather than an artificial equal per-job ceiling");
        string root = Path.Combine(Path.GetTempPath(), "pixinit-simple-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var consent = new ReviewOnlyConsent();
            var reviewed = new BenchmarkViewModel(new FileBenchmarkEngine(), () => [], () => null, () => { }, consent)
            { TargetResolver = WorkflowTestTarget, TargetDirectory = root, FileSizeMiB = 512, Iterations = 5, TimedMode = false, SustainedWrite = true };
            await reviewed.RunSimpleAsync(BenchmarkProfiles.Rnd4KQ1T1);
            Check(consent.Configuration is { Unbuffered: true, TimedMode: true, DurationSeconds: 5, Iterations: 5, FileSizeBytes: 536870912, Profiles: BenchmarkProfiles.Rnd4KQ1T1 } &&
                (consent.Configuration.Operations & BenchmarkOperation.SustainedWrite) == 0, "Row start enforces cache-bypass timed recipe while preserving chosen passes and file size");
            Check(reviewed.Result is null && reviewed.ConfigurationEditable && reviewed.MainBenchmarkLabel == "All" && !Directory.EnumerateFileSystemEntries(root).Any(), "Declining simplified start creates no benchmark file and restores editable controls");
            var config = BenchmarkPolicy.Quick with { Unbuffered = true, FileSizeBytes = 1048576, Profiles = BenchmarkProfiles.Rnd4KQ32T1 | BenchmarkProfiles.Seq128KQ32T1, Operations = BenchmarkOperation.SequentialRead | BenchmarkOperation.RandomRead, Iterations = 1, RandomOperationCount = 32 };
            var target = WorkflowTestTarget(root, []);
            var publishedPasses = new List<BenchmarkPassResult>();
            var session = await new FileBenchmarkEngine().RunAsync(target, config, true, new InlineProgress(p => { if (p.CompletedPass is { } pass) publishedPasses.Add(pass); }));
            Check(session.Completion == BenchmarkCompletion.Completed && session.PassResults.Count == 2 && session.PreparationBytesWritten == 1048576 && session.CleanupSucceeded, "New SATA Q32T1 and NVMe 128 KiB profiles run on one disposable MiB with reads only and clean up");
            Check(publishedPasses.SequenceEqual(session.PassResults), "Progress publishes the same validated pass records used by the final summary");
            BenchmarkPassResult Pass(int index, double speed) => new("SEQ1M Q8T1", index, 8, 1,
                BenchmarkOperationResult.Calculate(BenchmarkOperation.SequentialWrite, (long)(speed * 1_000_000), 1, Stopwatch.Frequency, null));
            session = session with { Configuration = config with { Iterations = 3 }, PassResults = [Pass(1, 910.41), Pass(2, 805.65), Pass(3, 14.53)] };
            var cell = BenchmarkViewModel.Cell(session, "SEQ1M Q8T1", BenchmarkOperation.SequentialWrite);
            Check(cell.Value == 576.86.ToString("F2") && cell.Detail.Contains("3/3") && cell.Tooltip.Contains("Pass 3") && cell.Tooltip.Contains("910.41"), "Primary average retains slow valid passes and tooltip retains Best plus individual evidence");
            var live = BenchmarkViewModel.CellFromPasses(session.PassResults.Take(1).ToArray(), 3, "SEQ1M Q8T1", BenchmarkOperation.SequentialWrite, true);
            Check(live.Value == 910.41.ToString("F2") && live.Detail.Contains("1/3") && live.Detail.Contains("Running"), "Running cells show provisional completed-pass averages with explicit running and pass-count labels");
            session = session with { PassResults = session.PassResults.Select(p => p.Pass == 3 ? p with { IncludeInAverage = false } : p).ToArray() };
            Check(BenchmarkViewModel.Cell(session, "SEQ1M Q8T1", BenchmarkOperation.SequentialWrite).Detail.Contains("2/3"), "Invalid attribution exclusions remain explicit in the completed pass count");
            var incomplete = Pass(1, 12) with { Measurement = Pass(1, 12).Measurement with { State = BenchmarkOperationState.Failed, Error = "Test failure" } };
            Check(BenchmarkViewModel.Cell(session with { PassResults = [incomplete] }, incomplete.Profile, BenchmarkOperation.SequentialWrite) is { Value: "Incomplete", Tooltip: var tip } && tip.Contains("Test failure"), "Incomplete measurements show their reason instead of a fabricated numeric score");
            Check(BenchmarkViewModel.Cell(null, "SEQ1M Q8T1", BenchmarkOperation.SequentialRead).Value == "--", "Empty results use a placeholder rather than zero speed");
        }
        finally { Directory.Delete(root, true); }
    }
}
