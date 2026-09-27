using System.IO;
using System.Numerics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using pixinit.Application.Reporting;
using pixinit.Core.Assessment;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Core.History;
using pixinit.Infrastructure.History;
using pixinit.Infrastructure.Windows.Sata;

namespace pixinit.Tests;

internal static partial class Program
{
    private static NvmeDiagnostics AssessedNvme(NvmeHealth? h, NvmeController? c = null)
    {
        c ??= NvmeControllerParser.Parse(SyntheticController());
        var responses = new[] { new NvmeResponse(NvmeOperation.Health, h is null ? NvmeOutcome.Unsupported : NvmeOutcome.Success, h is null ? "Synthetic unavailable" : "Synthetic success", h is null ? [] : SyntheticHealth(), DateTimeOffset.UtcNow, "Synthetic controller scope") };
        var result = NvmeResultMapper.Map(SyntheticNvme(), c, h, [], responses);
        return result with { Assessment = AssessmentPolicies.Assess(result) };
    }
    private static async Task Phase5Tests()
    {
        static byte[] HealthyHealth()
        {
            var bytes = SyntheticHealth();
            bytes[0] = 0;
            bytes[5] = 50;
            Array.Clear(bytes, 160, 16);
            return bytes;
        }
        var healthy = AssessedNvme(NvmeHealthParser.Parse(HealthyHealth()));
        Check(healthy.Assessment is { State: AssessmentState.NoIssuesDetected, Coverage: AssessmentCoverage.Complete, Checklist: ChecklistResult.Pass }, "NVMe complete passing evidence produces No Issues Detected and checklist PASS");
        Check(healthy.Assessment!.Explanation.Contains("No remaining-health") && !healthy.Assessment.Explanation.Contains("Health Remaining"), "Assessment does not calculate generic remaining health");
        var missing = AssessedNvme(null);
        Check(missing.Assessment is { State: AssessmentState.Unknown, Coverage: AssessmentCoverage.Insufficient, Checklist: ChecklistResult.Inconclusive }, "Missing mandatory NVMe evidence produces Unknown and INCONCLUSIVE");
        var warnBytes = SyntheticHealth(); warnBytes[0] = 0x04; var warning = AssessedNvme(NvmeHealthParser.Parse(warnBytes));
        Check(warning.Assessment is { State: AssessmentState.Critical, Checklist: ChecklistResult.Fail }, "Definite reliability warning produces Critical FAIL");
        var partialController = NvmeControllerParser.Parse(SyntheticController()) with { WarningKelvin = 0, CriticalKelvin = 0 };
        var partialWarn = AssessedNvme(NvmeHealthParser.Parse(warnBytes), partialController);
        Check(partialWarn.Assessment is { State: AssessmentState.Critical, Coverage: AssessmentCoverage.Partial, Checklist: ChecklistResult.Fail }, "Definite failure is not hidden by partial coverage");
        var spareBytes = HealthyHealth(); spareBytes[3] = 5; spareBytes[4] = 10; var spare = AssessedNvme(NvmeHealthParser.Parse(spareBytes));
        Check(spare.Assessment!.Checks.Single(c => c.RuleId == "NVME.AVAILABLE_SPARE").Outcome == CheckOutcome.Fail, "Available Spare below reported threshold is Warning evidence");
        var usedBytes = HealthyHealth(); usedBytes[5] = 120; var used = AssessedNvme(NvmeHealthParser.Parse(usedBytes));
        Check(used.Assessment is { State: AssessmentState.Attention, Checklist: ChecklistResult.PassWithAttention }, "Percentage Used at or above 100 produces attention without universal failure");
        var historyBytes = HealthyHealth(); historyBytes[144] = 3; historyBytes[176] = 4; var counters = AssessedNvme(NvmeHealthParser.Parse(historyBytes));
        Check(counters.Assessment!.Checks.Single(c => c.RuleId == "NVME.UNSAFE_SHUTDOWNS").Outcome == CheckOutcome.NotApplicable && counters.Assessment.Checks.Single(c => c.RuleId == "NVME.ERROR_LOG_COUNT").Outcome == CheckOutcome.NotApplicable, "Unsafe shutdown and broad error counters do not become unexplained failures");
        var sata = await new WindowsSataDiagnosticsProvider(new SyntheticTransport()).ReadAsync(SyntheticSata(), default);
        Check(sata.Assessment is { Coverage: AssessmentCoverage.Complete, Checklist: ChecklistResult.Pass }, "SATA validated SMART status/data/thresholds meet checklist PASS");
        Check(sata.Assessment!.Checks.Single(c => c.RuleId == "SATA.VENDOR_RAW").Outcome == CheckOutcome.NotApplicable, "Unknown SATA vendor raw attributes are excluded from assessment");
        var sataFailed = sata with { Commands = sata.Commands.Select(c => c.Operation == AtaOperation.SmartStatus ? c with { Registers = new byte[] { 0, 0, 0, 0xF4, 0x2C, 0, 0x50, 0 } } : c).ToArray() };
        var sf = AssessmentPolicies.Assess(sataFailed); Check(sf is { State: AssessmentState.Critical, Checklist: ChecklistResult.Fail }, "SATA device-reported threshold failure produces Critical FAIL");

        string root = Path.Combine(Path.GetTempPath(), "pixinit-phase5-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string db = Path.Combine(root, "history.db"); var store = new SqliteHistoryStore(db); await store.InitializeAsync();
            Check(await store.GetRetentionDaysAsync() == 2, "SQLite schema creates original two-day retention default");
            var snapshot = SnapshotFactory.Create(Guid.NewGuid(), SyntheticNvme(), healthy);
            Check(snapshot.Metrics.Single(m => m.Key == "nvme.composite_temperature").ValueText == healthy.Temperature.Value!.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture), "Snapshot retains invariant round-trip precision for converted metrics");
            var huge = BigInteger.One << 100; snapshot = snapshot with { Metrics = snapshot.Metrics.Append(new("test.uint128", huge.ToString(), "count", "Available", "Synthetic", snapshot.ObservedAtUtc, snapshot.Scope)).ToArray() };
            await store.SaveAsync(snapshot); var loaded = await store.LoadAsync(snapshot.Id);
            Check(loaded?.Metrics.Single(m => m.Key == "test.uint128").ValueText == huge.ToString(), "BigInteger database round-trip is lossless decimal text");
            Check(loaded!.Queries.Any(q => q.Raw.Length > 0), "Raw payloads survive SQLite round-trip");
            Check((await store.ListAsync()).Single().CompletionState == "Completed", "Historical row is explicitly completed and distinct from live state");
            var cancelled = SnapshotFactory.CreateAttempt(Guid.NewGuid(), SyntheticNvme(), "Cancelled", "Synthetic cancellation");
            var partial = SnapshotFactory.Create(Guid.NewGuid(), SyntheticNvme(), missing);
            await store.SaveAsync(cancelled); await store.SaveAsync(partial);
            var explicitStates = await store.ListAsync();
            Check(explicitStates.Any(r => r.Id == cancelled.Id && r.CompletionState == "Cancelled" && r.Assessment == "Unknown") && explicitStates.Any(r => r.Id == partial.Id && r.CompletionState == "Partial"), "Partial and cancelled sessions are persisted explicitly and never labelled completed");
            Check((await store.ListAsync(protocol: "Nvme", deviceKey: snapshot.Device.MatchKey)).All(r => r.Protocol == "Nvme" && r.IdentityReliable), "History filtering uses protocol and reliable device identity");
            var restarted = new SqliteHistoryStore(db); await restarted.InitializeAsync(); Check((await restarted.LoadAsync(snapshot.Id)) is not null, "History reopens after store restart");
            var rollback = new SqliteHistoryStore(Path.Combine(root, "rollback.db")); await rollback.InitializeAsync(); rollback.FailAfterSessionInsertForTest = true;
            bool failed = false; try { await rollback.SaveAsync(snapshot with { Id = Guid.NewGuid() }); } catch (InvalidOperationException) { failed = true; }
            Check(failed && await rollback.CountAsync() == 0, "Snapshot transaction rolls back after forced failure");
            string futureDb = Path.Combine(root, "future.db"); await using (var c = new SqliteConnection($"Data Source={futureDb}")) { await c.OpenAsync(); await new SqliteCommand("PRAGMA user_version=99;", c).ExecuteNonQueryAsync(); }
            failed = false; try { await new SqliteHistoryStore(futureDb).InitializeAsync(); } catch (InvalidOperationException) { failed = true; }
            Check(failed && File.Exists(futureDb), "Unsupported migration fails without deleting database");
            var cleanupNow = DateTimeOffset.UtcNow;
            var old = snapshot with { Id = Guid.NewGuid(), ObservedAtUtc = cleanupNow.AddDays(-2) }; await store.SaveAsync(old);
            var boundary = snapshot with { Id = Guid.NewGuid(), ObservedAtUtc = cleanupNow.AddDays(-1) }; await store.SaveAsync(boundary);
            int removed = await store.CleanupAsync(cleanupNow, 1); Check(removed == 1 && await store.LoadAsync(boundary.Id) is not null, "Retention removes only records strictly older than cutoff transactionally");
            var unreliable = snapshot with { Id = Guid.NewGuid(), Device = snapshot.Device with { IdentityReliable = false, MatchKey = null }, ObservedAtUtc = snapshot.ObservedAtUtc.AddMinutes(1) };
            Check(SnapshotComparison.Compare(snapshot, unreliable).Count == 0, "Ambiguous identities disable automatic comparison");
            var next = snapshot with { Id = Guid.NewGuid(), ObservedAtUtc = snapshot.ObservedAtUtc.AddMinutes(2), Metrics = snapshot.Metrics.Select(m => m.Key == "test.uint128" ? m with { ValueText = (huge + 7).ToString() } : m).ToArray() };
            Check(SnapshotComparison.Compare(snapshot, next).Single(m => m.Key == "test.uint128").Delta == "7", "Scope-compatible ordered snapshots compute lossless counter delta");
            var decrease = next with { Id = Guid.NewGuid(), ObservedAtUtc = next.ObservedAtUtc.AddMinutes(1), Metrics = next.Metrics.Select(m => m.Key == "test.uint128" ? m with { ValueText = "1" } : m).ToArray() };
            Check(SnapshotComparison.Compare(next, decrease).Single(m => m.Key == "test.uint128").Status.Contains("Reset"), "Decreasing counter is marked reset or incompatible without manufactured delta");
            Check(SnapshotComparison.Compare(snapshot, next with { Scope = "Different scope" }).Count == 0, "Different metric scope prevents comparison");
            string json = Path.Combine(root, "report.json"), txt = Path.Combine(root, "report.txt");
            await DiagnosticReportExporter.ExportJsonAsync(snapshot, json, new()); await DiagnosticReportExporter.ExportTextAsync(snapshot, txt, new());
            string jsonText = await File.ReadAllTextAsync(json), txtText = await File.ReadAllTextAsync(txt);
            Check(jsonText.Contains(huge.ToString()) && jsonText.Contains("REDACTED") && !jsonText.Contains("SYNTHETIC-NVME\""), "JSON export preserves large integer strings and redacts serial");
            Check(!jsonText.Contains(Convert.ToBase64String(snapshot.Queries[0].Raw)) && jsonText.Contains("RawPayloadsIncluded\": false"), "JSON raw payload export is optional and disabled by default");
            string shared = Path.Combine(root, "explicit-options.json"); await DiagnosticReportExporter.ExportJsonAsync(snapshot, shared, new(false, true)); string sharedText = await File.ReadAllTextAsync(shared);
            Check(sharedText.Contains(snapshot.Device.Serial!) && sharedText.Contains(Convert.ToBase64String(snapshot.Queries[0].Raw)) && sharedText.Contains("RawPayloadsIncluded\": true"), "Explicit export options can retain serial and include clearly labelled raw payloads");
            Check(txtText.Contains("INCONCLUSIVE") == (snapshot.Assessment.Checklist == ChecklistResult.Inconclusive) && txtText.Contains(snapshot.RuleSetVersion), "TXT export preserves original assessment and rule version");
            bool invalidPath = false; try { await DiagnosticReportExporter.ExportJsonAsync(snapshot, Path.Combine(root, "missing", "a.json"), new()); } catch (DirectoryNotFoundException) { invalidPath = true; }
            Check(invalidPath && File.Exists(db), "Invalid export destination does not affect stored snapshot");
            var badStore = new SqliteHistoryStore(root); bool dbFailure = false; try { await badStore.InitializeAsync(); } catch { dbFailure = true; }
            Check(dbFailure && healthy.Assessment is not null, "Database failure leaves live diagnostic assessment usable");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
