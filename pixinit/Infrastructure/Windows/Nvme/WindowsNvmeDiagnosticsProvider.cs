using System.IO;
using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Core.Assessment;

namespace pixinit.Infrastructure.Windows.Nvme;

public sealed class WindowsNvmeDiagnosticsProvider : INvmeDiagnosticsProvider
{
    private readonly INvmeTransport transport;
    public WindowsNvmeDiagnosticsProvider() : this(new WindowsNvmeTransport()) { }
    internal WindowsNvmeDiagnosticsProvider(INvmeTransport transport) => this.transport = transport;
    public Task<NvmeDiagnostics> ReadAsync(StorageDevice device, CancellationToken token, IProgress<string>? progress = null)
    {
        NvmeIdentityGuard.Require(device);
        NvmeController? controller = null; NvmeHealth? health = null; IReadOnlyList<NvmeError>? errors = null;
        var responses = new List<NvmeResponse>();
        void Read(NvmeOperation op, string stage, Action<byte[]> parse)
        {
            token.ThrowIfCancellationRequested(); progress?.Report(stage);
            var response = transport.Query(device, op, token);
            if (response.Succeeded) try { parse(response.Payload); } catch (InvalidDataException ex) { response = response with { Outcome = NvmeOutcome.InvalidResponse, Explanation = ex.Message }; }
            responses.Add(response); token.ThrowIfCancellationRequested();
        }
        void Missing(NvmeOperation op, string reason) => responses.Add(new(op, NvmeOutcome.NotQueried, reason, [], DateTimeOffset.UtcNow,
            op == NvmeOperation.Namespace ? "Namespace unavailable; no numeric namespace ID established" : NvmeQueryPolicy.Scope(op)));
        Read(NvmeOperation.Controller, "Reading controller identity", b => controller = NvmeControllerParser.Parse(b));
        Missing(NvmeOperation.Namespace, "No verified namespace ID mapping; namespace query withheld.");
        if (responses[0].Outcome == NvmeOutcome.IdentityMismatch)
        { Missing(NvmeOperation.Health, "Identity mismatch; rescan before reading."); Missing(NvmeOperation.Errors, "Identity mismatch; query withheld."); }
        else
        {
            Read(NvmeOperation.Health, "Reading SMART/Health information", b => health = NvmeHealthParser.Parse(b));
            if (responses[^1].Operation == NvmeOperation.Health)
                responses[^1] = responses[^1] with { Scope = controller is not null && (controller.LogAttributes & 1) != 0
                    ? "Selected device/namespace through Windows (controller reports per-namespace SMART support; numeric NSID unavailable)"
                    : "Controller-wide (per-namespace SMART support not reported); not exclusive to the selected namespace" };
            // Microsoft adapter log queries require 512-byte chunks, i.e. eight 64-byte entries.
            if (controller?.ErrorSlots >= 8) Read(NvmeOperation.Errors, "Reading bounded error information", b => errors = NvmeErrorParser.Parse(b, 8));
            else Missing(NvmeOperation.Errors, "Controller capacity for eight entries not established; Windows adapter query requires a 512-byte chunk. No over-capability request sent.");
        }
        token.ThrowIfCancellationRequested(); progress?.Report("Preparing NVMe results");
        var mapped = NvmeResultMapper.Map(device, controller, health, errors, responses);
        var assessment = AssessmentPolicies.Assess(mapped);
        return Task.FromResult(mapped with { Assessment = assessment, Summary = $"Read finished · {responses.Count(r => r.Succeeded)}/{responses.Count} successful queries · Assessment {assessment.StateDisplay} · Checklist {assessment.ChecklistDisplay}" });
    }
}
