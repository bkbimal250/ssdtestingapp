using System.Diagnostics;
using System.IO;
using pixinit.Core.Benchmark;

namespace pixinit.Infrastructure.Benchmarking;

public sealed partial class FileBenchmarkEngine
{
    private sealed class ForwardProgress(Action<BenchmarkProgress> action) : IProgress<BenchmarkProgress> { public void Report(BenchmarkProgress value) => action(value); }
    private async Task<BenchmarkSession> RunDirectAsync(BenchmarkTarget target, BenchmarkConfiguration configuration, bool consentGranted, IProgress<BenchmarkProgress>? progress, CancellationToken token)
    {
        configuration = configuration with { IoMode = DirectBenchmarkPolicy.Describe(configuration) };
        DirectBenchmarkPolicy.Validate(configuration, target.AvailableBytes);
        if (!consentGranted) throw new InvalidOperationException("Unbuffered tests require explicit consent for preparation and all selected passes.");
        if (target.VolumeRoot.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidOperationException("Unbuffered tests require a local filesystem volume; network targets are not supported.");
        Guid id = configuration.FilePlan?.SessionId ?? Guid.NewGuid(); DateTimeOffset started = DateTimeOffset.UtcNow; long total = Stopwatch.GetTimestamp();
        var owned = await files.CreateAsync(target.Directory, id, token, configuration.FilePlan?.Nonce);
        var passes = new List<BenchmarkPassResult>(); var samples = new List<SpeedSample>(); var results = new List<BenchmarkOperationResult>();
        long prepBytes = 0; double prepSeconds = 0, cleanupSeconds = 0; bool cleaned = false; string? reason = null;
        var completion = BenchmarkCompletion.Completed; string alignment = "Not established"; uint sectorSize = 0, bufferAlignment = 0;
        var selected = BenchmarkProfile.All.Where(p => (configuration.Profiles & p.Id) != 0).ToArray();
        var jobs = selected.SelectMany(p => new[] { p.Random ? BenchmarkOperation.RandomWrite : BenchmarkOperation.SequentialWrite, p.Random ? BenchmarkOperation.RandomRead : BenchmarkOperation.SequentialRead }.Where(op => (configuration.Operations & op) != 0).Select(op => (Profile: p, Operation: op))).ToArray();
        int jobsDone = 0, jobsTotal = jobs.Length * configuration.Iterations;
        try
        {
            if (!await files.IsOwnedAsync(owned, token)) throw new IOException("Benchmark ownership invalid; write refused.");
            using var file = new NativeBenchmarkFile(owned, configuration.RequiredFileBytes);
            sectorSize = file.SectorSize; bufferAlignment = Math.Max(4096, file.Alignment);
            alignment = $"Native buffer base aligned to 64 KiB; storage alignment {file.Alignment} bytes. Flags 0x{NativeBenchmarkFile.Flags:X8}.";
            long prepStart = Stopwatch.GetTimestamp();
            if (configuration.HasReadWork || configuration.TimedMode) await Task.Run(() => file.Prepare(configuration.FileSizeBytes, progress, token, n => prepBytes += n), token);
            prepSeconds = Elapsed(prepStart);
            for (int pass = 1; pass <= configuration.Iterations; pass++)
            {
                foreach (var job in jobs)
                {
                    token.ThrowIfCancellationRequested();
                    bool writing = job.Operation is BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite;
                    long count = configuration.TimedMode ? (writing ? DirectBenchmarkPolicy.TimedWriteAllowance(configuration) / job.Profile.BlockBytes : long.MaxValue / job.Profile.BlockBytes) : job.Profile.Random ? configuration.RandomOperationCount : configuration.FileSizeBytes / job.Profile.BlockBytes;
                    var previous = passes.Where(r => r.Profile == job.Profile.Name && r.Measurement.Operation == job.Operation && r.Measurement.State == BenchmarkOperationState.Completed && r.IncludeInAverage).ToArray();
                    var forwarded = new ForwardProgress(p => progress?.Report(p with { Stage = $"Pass {pass}/{configuration.Iterations} - {job.Profile.Name} {job.Operation}", Pass = pass, TotalPasses = configuration.Iterations, OverallPercent = jobsTotal == 0 ? 0 : (jobsDone + p.Percent / 100) * 100 / jobsTotal, CompletedAverageMBs = previous.Length == 0 ? null : previous.Average(r => r.Measurement.MegabytesPerSecond) }));
                    forwarded.Report(new("Starting profile", job.Operation, 0, count * job.Profile.BlockBytes, 0));
                    var measured = await Task.Run(() => file.Measure(job.Profile, job.Operation, configuration.FileSizeBytes, count, forwarded, token, configuration.TimedMode ? configuration.DurationSeconds : 0));
                    bool invalid = DirectBenchmarkPolicy.Plausibility(target, [measured]).StartsWith("INVALID", StringComparison.Ordinal);
                    passes.Add(new(job.Profile.Name, pass, job.Profile.QueuePerWorker, job.Profile.Workers, measured) { IncludeInAverage = !invalid, IsValidForDeviceAttribution = target.PhysicalIdentityReliable && !invalid });
                    forwarded.Report(new("Profile finished", job.Operation, measured.BytesProcessed, count * job.Profile.BlockBytes, measured.MeasuredSeconds) { DurationSeconds = configuration.TimedMode ? configuration.DurationSeconds : 0, CurrentMBs = measured.MegabytesPerSecond }); jobsDone++;
                    if (measured.State != BenchmarkOperationState.Completed) { completion = measured.State == BenchmarkOperationState.Cancelled ? BenchmarkCompletion.Cancelled : BenchmarkCompletion.Failed; reason = measured.Error; break; }
                }
                if (completion != BenchmarkCompletion.Completed) break;
                if (pass < configuration.Iterations) await Preconditioning.RecoverAsync(token);
            }
            if (completion == BenchmarkCompletion.Completed && (configuration.Operations & BenchmarkOperation.SustainedWrite) != 0)
            {
                var sustained = await Task.Run(() => file.Sustained(configuration, progress, token)); results.Add(sustained.Result); samples = sustained.Samples;
                if (sustained.Result.State != BenchmarkOperationState.Completed) { completion = sustained.Result.State == BenchmarkOperationState.Cancelled ? BenchmarkCompletion.Cancelled : BenchmarkCompletion.Failed; reason = sustained.Result.Error; }
            }
        }
        catch (OperationCanceledException) { completion = BenchmarkCompletion.Cancelled; reason = "Cancelled; incomplete passes are not included in averages."; }
        catch (Exception ex) { completion = BenchmarkCompletion.Failed; reason = ex.Message; }
        finally
        {
            long start = Stopwatch.GetTimestamp();
            try { await files.CleanupAsync(owned); cleaned = true; }
            catch (Exception ex) { reason = $"{reason} Cleanup failed: {ex.Message}; owned path: {owned.FilePath}"; }
            cleanupSeconds = Elapsed(start);
        }
        // The legacy summary fields use Q1T1 only; per-profile records retain every queue's measurements.
        foreach (var group in passes.Where(p => p.Profile.EndsWith("Q1T1", StringComparison.Ordinal)).GroupBy(p => p.Measurement.Operation))
        {
            var measured = group.Select(p => p.Measurement).ToArray();
            bool complete = measured.Length == configuration.Iterations && measured.All(r => r.State == BenchmarkOperationState.Completed);
            var aggregate = BenchmarkOperationResult.Calculate(group.Key, measured.Sum(r => r.BytesProcessed), measured.Sum(r => r.CompletedOperations), (long)(measured.Sum(r => r.MeasuredSeconds) * Stopwatch.Frequency), null,
                complete ? BenchmarkOperationState.Completed : completion == BenchmarkCompletion.Cancelled ? BenchmarkOperationState.Cancelled : BenchmarkOperationState.Failed);
            results.Add(aggregate);
        }
        var drop = AnalyzeSustained(samples);
        string warning = DirectBenchmarkPolicy.Plausibility(target, passes.Select(p => p.Measurement));
        return new(id, started, DateTimeOffset.UtcNow, configuration.TimedMode ? "benchmark-policy-unbuffered-timed-v2" : "benchmark-policy-unbuffered-v1", typeof(FileBenchmarkEngine).Assembly.GetName().Version?.ToString() ?? "Unavailable", target, configuration, results, completion, reason, prepBytes, prepSeconds, 0, cleanupSeconds, Elapsed(total), true, consentGranted, null, null, owned.FilePath, cleaned,
            DirectBenchmarkPolicy.Describe(configuration) + " " + alignment + " Cryptographic random preparation per MiB; distinct random buffers per outstanding request, reused during timing. Two-second pause is not guaranteed SLC recovery. ")
        {
            PassResults = passes, ValidationWarning = warning, SectorSize = sectorSize, BufferAlignment = bufferAlignment, TrimAttempted = false, TrimStatus = Preconditioning.TrimStatus, SafetyPolicy = "unbuffered-96GiB-ordinary-32GiB-sustained-reserve3GiB-v1", SustainedSamples = samples,
            SustainedWriteMBs = completion == BenchmarkCompletion.Completed ? drop.MBs : null, ThroughputDropSecond = drop.Second,
            SustainedStatus = samples.Count == 0 ? "Not run" : drop.Second is null ? "No qualifying drop - showing overall average; bounded by time and byte limit" : "Drop candidate only; not proof of SLC exhaustion"
        };
    }
}