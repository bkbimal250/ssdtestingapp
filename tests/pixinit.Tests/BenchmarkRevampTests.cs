using pixinit.Application.Discovery;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Infrastructure.Benchmarking;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.ViewModels.Benchmark;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;
internal static partial class Program
{
    private static async Task BenchmarkRevampTests()
    {
        pixinit.Core.Benchmark.BenchmarkPassResult WritePass(int pass, double speed, long bytes) => new("SEQ1M Q8T1", pass, 8, 1,
            new(pixinit.Core.Benchmark.BenchmarkOperation.SequentialWrite, pixinit.Core.Benchmark.BenchmarkOperationState.Completed,
                bytes, 1, 5.99, speed * 1_000_000, speed, speed * 1_000_000 / 1048576, null, null, null));
        var writePasses = new[] { WritePass(1, 910.41, 5L * 1073741824), WritePass(2, 805.65, 4L * 1073741824), WritePass(3, 14.53, 86973088) };
        var filtered = WritePassPresentation.Create(writePasses);
        Check(filtered.FilteredAverage is double avg && Math.Abs(avg - 858.03) < .0001 && filtered.Retained == 2 && filtered.Total == 3, "Low-write outlier produces a separate 858.03 filtered average over two of three passes");
        var measured = pixinit.Core.Benchmark.DirectBenchmarkPolicy.Summarize(writePasses).Single();
        Check(Math.Abs(measured.AverageMBs - 576.863333333) < .0001 && measured.BestMBs == 910.41 && writePasses[2].IncludeInAverage, "Measured averages, Best and original inclusion flags remain unchanged in reports/history");
        Check(filtered.Tooltip.Contains("Pass 3") && filtered.Tooltip.Contains("cache exhaustion not confirmed"), "Tooltip retains low pass evidence without claiming a proven cache cause");
        Check(WritePassPresentation.Create([WritePass(1, 14.53, 86973088), WritePass(2, 20, 1073741824)]).FilteredAverage is null, "Uniformly slow writes are not filtered without a comparable fast pass");
        Check(!WritePassPresentation.IsLowOutlier(writePasses[2] with { Profile = "SEQ1M Q1T1" }, writePasses), "Different queue profiles cannot justify outlier filtering");
        Check(!WritePassPresentation.IsLowOutlier(writePasses[2] with { Measurement = writePasses[2].Measurement with { Operation = pixinit.Core.Benchmark.BenchmarkOperation.SequentialRead } }, writePasses), "Read measurements are not filtered by the write rule");
        Check(!WritePassPresentation.IsLowOutlier(writePasses[2] with { IncludeInAverage = false }, writePasses), "Existing invalid or budget-excluded passes are not reclassified");
        Check(WritePassPresentation.IsLowOutlier(WritePass(3, 99, 100000000), writePasses) && !WritePassPresentation.IsLowOutlier(WritePass(3, 100, 536870912), writePasses), "Write filter respects strict byte and speed boundaries");
        Check(!WritePassPresentation.IsLowOutlier(WritePass(3, 120, 100000000), writePasses) && !WritePassPresentation.IsLowOutlier(WritePass(3, 20, 1073741824), writePasses), "Filtering requires both low bytes and low speed, not either condition alone");
        Check(BenchmarkViewModel.SmartDuration(30, 60, 19L * 1073741824) == 5 && BenchmarkViewModel.SmartDuration(30, 60, 20L * 1073741824) == 30 && BenchmarkViewModel.SmartDuration(30, 91, 40L * 1073741824) == 5, "Sustained caps for low absolute free space as well as low free percentage");
        Check(BenchmarkProcessLease.IsCompetingProcess("diskmark64") && BenchmarkProcessLease.IsCompetingProcess("DiskMark32") && !BenchmarkProcessLease.IsCompetingProcess("explorer"), "CrystalDiskMark process detection recognizes supported executable names");
        string lockDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixinit-lock-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(lockDirectory);
        string lockPath = System.IO.Path.Combine(lockDirectory, "PIXINIT_BENCHMARK_LOCK");
        try
        {
            using (var firstLease = BenchmarkProcessLease.Acquire(lockPath))
            {
                bool secondBlocked = false; try { using var second = BenchmarkProcessLease.Acquire(lockPath); } catch (InvalidOperationException) { secondBlocked = true; }
                Check(secondBlocked && System.IO.File.Exists(lockPath), "Exclusive owned lock blocks concurrent PIXINIT acquisition");
            }
            Check(!System.IO.File.Exists(lockPath), "Owned lock is deleted on normal disposal");
            try { using var lease = BenchmarkProcessLease.Acquire(lockPath); throw new OperationCanceledException(); } catch (OperationCanceledException) { }
            Check(!System.IO.File.Exists(lockPath), "Owned lock releases during cancellation/failure unwinding");
            System.IO.File.WriteAllText(lockPath, "unowned sentinel");
            bool existingBlocked = false; try { using var lease = BenchmarkProcessLease.Acquire(lockPath); } catch (InvalidOperationException) { existingBlocked = true; }
            Check(existingBlocked && System.IO.File.ReadAllText(lockPath) == "unowned sentinel", "Existing lock is preserved rather than overwritten or deleted");
            System.IO.File.Delete(lockPath);
        }
        finally { System.IO.Directory.Delete(lockDirectory); }
        var vm = new BenchmarkViewModel(new FileBenchmarkEngine(), () => [], () => null, () => { });
        Check(vm.TargetResolver.Method.DeclaringType == typeof(pixinit.Application.Benchmarking.BenchmarkTargetResolver), "Production view model defaults to the real Windows filesystem target resolver");
        string lowSpaceRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixinit-low-space-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(lowSpaceRoot);
        try
        {
            var lowConsent = new Consent(true);
            var lowVm = new BenchmarkViewModel(new FileBenchmarkEngine(), () => [], () => null, () => { }, lowConsent)
            {
                UseUnbuffered = false, TargetDirectory = lowSpaceRoot,
                TargetResolver = (path, disks) => pixinit.Application.Benchmarking.BenchmarkTargetResolver.Resolve(path, disks) with { AvailableBytes = 10L * 1073741824 }
            };
            await lowVm.RunAsync();
            Check(lowConsent.Calls == 0 && lowVm.Result is null && lowVm.BlockingWarning.Contains("more than 10 GiB") && System.IO.Directory.GetFiles(lowSpaceRoot).Length == 0,
                "Low-space start refuses consent and creates no benchmark file with the production threshold intact");
        }
        finally { System.IO.Directory.Delete(lowSpaceRoot); }

        Check(vm.Iterations == 1 && !vm.TimedMode && vm.FileSizeMiB == 512 && !vm.SustainedWrite && vm.UseUnbuffered, "Quick defaults use one fixed-workload pass, a 512 MiB file and no sustained writes");
        Check(vm.Configuration().RequiredFileBytes == 512L * 1048576 && vm.Configuration().MaximumWriteBytes() < 2L * 1073741824, "Quick cumulative application writes stay below 2 GiB and peak benchmark-file allocation is 512 MiB without running it");
        bool cloudBlocked = false;
        try { BenchmarkViewModel.ValidateBenchmarkLocation(@"C:\Users\example\OneDrive\Desktop\test"); } catch (InvalidOperationException) { cloudBlocked = true; }
        BenchmarkViewModel.ValidateBenchmarkLocation(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixinit-local"));
        Check(cloudBlocked, "OneDrive target is refused before preparation while local targets remain available");
        vm.SetSuggestedTarget(SyntheticNvme() with { MountPoints = [System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!] });
        Check(vm.TargetDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase), "Suggested target on system volume is local application data, not redirected Desktop");
        vm.DeepModeCommand.Execute(null); Check(vm.SustainedDurationSeconds == 30 && vm.SustainedBudgetMiB == 16384, "Deep changes sustained duration without enlarging its byte ceiling");
        vm.QuickModeCommand.Execute(null); Check(!vm.SustainedWrite && !vm.TimedMode && vm.Configuration().Unbuffered, "Quick restores bounded native workloads without enabling sustained");
        Check(BenchmarkViewModel.SmartDuration(30, 89) == 5 && BenchmarkViewModel.SmartDuration(15, 80) == 15 && BenchmarkViewModel.SmartDuration(15, null) == 15, "Smart duration uses observed volume fullness with an exact greater-than-80 threshold");
        bool refused = false; try { BenchmarkViewModel.ValidateStartSpace(10L * 1073741824); } catch (InvalidOperationException) { refused = true; }
        BenchmarkViewModel.ValidateStartSpace(10L * 1073741824 + 1);
        Check(refused, "Ten-GiB preflight rejects the boundary before consent or file preparation");
        Check(PcieLinkReader.IsValid(3, 4) && !PcieLinkReader.IsValid(0, 4) && !PcieLinkReader.IsValid(3, 0) && !PcieLinkReader.IsValid(99, 4), "PCIe display accepts valid observed link fields and rejects unavailable/unknown codes");
        Check(PcieLinkReader.Read("PIXINIT_NONEXISTENT_INSTANCE") is null, "Missing PnP instance does not produce a guessed PCIe link");
        var linked = SyntheticNvme() with { PcieGeneration = 3, PcieLaneWidth = 4, PcieLinkSource = "Synthetic Windows property fixture" };
        var snapshot = pixinit.Core.History.SnapshotFactory.DeviceSnapshot(linked);
        Check(snapshot.PcieGeneration == 3 && snapshot.PcieLaneWidth == 4 && snapshot.PcieLinkSource == linked.PcieLinkSource, "Discovery link values and provenance persist in diagnostic and benchmark device snapshots");
        var linkTarget = new pixinit.Core.Benchmark.BenchmarkTarget("C:\\", "C:\\", "NTFS", 20L * 1073741824, snapshot, "Synthetic mapping", true);
        string notice = pixinit.Core.Benchmark.DirectBenchmarkPolicy.Plausibility(linkTarget, []);
        Check(notice.Contains("PCIe 3.0 x4") && !notice.Contains("unavailable"), "Stored performance notice uses observed link metadata instead of falsely reporting it unavailable");
        Check(BenchmarkViewModel.LinkBadge(snapshot) == "NVMe Gen3 x4" && BenchmarkViewModel.LinkBadge(snapshot with { PcieGeneration = null, PcieLaneWidth = null }).Contains("not reported"), "Mode badge uses observed link fields and never estimates lanes from throughput");
        var ratedDevice = SyntheticNvme() with { Model = "INTEL SSDPEKNW512G8", CapacityBytes = 512_110_190_592 };
        var ratedDiagnostics = NvmeResultMapper.Map(ratedDevice, NvmeControllerParser.Parse(SyntheticController()) with { Model = "INTEL SSDPEKNW512G8" }, NvmeHealthParser.Parse(SyntheticHealth()), null, []);
        var rated = pixinit.Core.Diagnostics.Common.ManufacturerEnduranceCatalog.Apply(ratedDevice, ratedDiagnostics);
        Check(rated.Tbw is { RatedTB: 100, RatingVerified: true } && rated.Tbw.WrittenTB == ratedDiagnostics.Tbw!.WrittenTB, "Verified Intel 660p 512 GB catalog supplies 100 TB rating independently of SMART host-write measurement");
        var device = SyntheticNvme();
        var discovery = new ControlledDiscovery(); discovery.Completion.SetResult([device]);
        var shell = new ShellViewModel(new DiscoveryCoordinator(discovery)); await shell.ScanAsync();
        Check(shell.FooterSummary.Contains("Diagnostics not yet queried"), "Footer reports not queried before a diagnostic snapshot exists");
        var health = NvmeHealthParser.Parse(SyntheticHealth());
        shell.Nvme.Apply(NvmeResultMapper.Map(device, NvmeControllerParser.Parse(SyntheticController()), health, null, [new(NvmeOperation.Health, NvmeOutcome.Success, "Synthetic", SyntheticHealth(), DateTimeOffset.UtcNow, "Synthetic device scope")]));
        Check(shell.FooterSummary.Contains("Diagnostics queried") && !shell.FooterSummary.Contains("not yet queried") && shell.FooterSummary.Contains("TB host writes"), "Footer follows current diagnostic snapshot rather than stale discovery text");
        shell.Nvme.Apply(shell.Nvme.Result! with { Tbw = new(0) });
        Check(shell.FooterSummary.Contains("Diagnostics queried") && shell.FooterSummary.Contains("0.000 TB"), "Observed zero host writes is not mistaken for an unqueried diagnostic session");
        shell.Nvme.SetDevice(device with { Id = "another-drive" });
        Check(shell.FooterSummary.Contains("Diagnostics not yet queried"), "Device change clears the queried footer with the diagnostic snapshot");
        shell.Close();
    }
}
