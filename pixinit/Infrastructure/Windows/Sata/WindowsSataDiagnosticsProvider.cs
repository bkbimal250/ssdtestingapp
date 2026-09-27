using System.IO;
using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Assessment;
using pixinit.Infrastructure.Logging;

namespace pixinit.Infrastructure.Windows.Sata;

public sealed class WindowsSataDiagnosticsProvider : ISataDiagnosticsProvider
{
    private readonly IAtaTransport transport;
    public WindowsSataDiagnosticsProvider() : this(new WindowsAtaTransport()) { }
    internal WindowsSataDiagnosticsProvider(IAtaTransport transport) => this.transport = transport;
    public Task<SataDiagnostics> ReadAsync(StorageDevice device, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
        Task.FromResult(Read(device, cancellationToken, progress));

    private SataDiagnostics Read(StorageDevice device, CancellationToken token, IProgress<string>? progress)
    {
        SataIdentityGuard.RequireSata(device); // reject before even constructing a transport session
        var commands = new List<AtaCommandResult>();
        AtaIdentity? identity = null; SmartThresholds? thresholds = null; SmartAttributes? attributes = null;
        var notes = new List<string>();
        void Parse(AtaCommandResult command, Action parser)
        {
            if (!command.Succeeded) return;
            try { parser(); }
            catch (InvalidDataException ex)
            {
                int index = commands.IndexOf(command);
                commands[index] = command with { Outcome = AtaOutcome.InvalidResponse, Explanation = ex.Message };
            }
        }
        try
        {
            token.ThrowIfCancellationRequested(); progress?.Report("Opening selected drive · Validating identity");
            using var session = transport.Open(device, token);
            AtaCommandResult Run(AtaOperation operation, string stage)
            {
                token.ThrowIfCancellationRequested(); progress?.Report(stage);
                var response = session.Execute(operation, token); commands.Add(response);
                DiscoveryLog.Write($"SATA {operation}: {response.Outcome}; {response.Explanation}");
                token.ThrowIfCancellationRequested();
                return response;
            }
            var identify = Run(AtaOperation.Identify, "Reading identity");
            Parse(identify, () => identity = AtaIdentifyParser.Parse(identify.Payload));
            if (identity is not null)
            {
                if (!string.IsNullOrWhiteSpace(identity.Model) && !string.Equals(identity.Model.Trim(), device.Model.Trim(), StringComparison.Ordinal)) notes.Add("ATA model differs from Windows discovery; both sources retained.");
                if (!string.IsNullOrWhiteSpace(identity.Serial) && !string.IsNullOrWhiteSpace(device.Serial) && identity.Serial.Trim() != device.Serial.Trim()) notes.Add("ATA serial differs from Windows discovery; both sources retained; further commands withheld.");
                if (identity.Firmware is not null && device.Firmware is not null && identity.Firmware != device.Firmware) notes.Add("ATA firmware differs from Windows discovery; both sources retained.");
                if (identity.CapacityBytes is not null && device.CapacityBytes is not null && identity.CapacityBytes != device.CapacityBytes) notes.Add("ATA byte capacity differs from Windows discovery; neither source overwrites the other.");
            }
            if (identity is null || notes.Any(n => n.Contains("serial differs")))
                Skip(AtaOutcome.NotQueried, "SMART not queried because ATA identity could not be validated.");
            else if (identity.SmartSupported != true)
                Skip(identity.SmartSupported == false ? AtaOutcome.Unsupported : AtaOutcome.NotQueried,
                    identity.SmartSupported == false ? "SMART is not supported according to valid IDENTIFY data." : "SMART support validity not established; commands withheld.");
            else if (identity.SmartEnabled != true)
                Skip(identity.SmartEnabled == false ? AtaOutcome.SmartDisabled : AtaOutcome.NotQueried,
                    identity.SmartEnabled == false ? "SMART disabled. PIXINIT will not enable it." : "SMART enabled state unknown; commands withheld.");
            else
            {
                var data = Run(AtaOperation.SmartData, "Reading SMART attributes");
                var limits = Run(AtaOperation.SmartThresholds, "Reading thresholds");
                Parse(limits, () => thresholds = SmartThresholdParser.Parse(limits.Payload));
                Parse(data, () => attributes = SmartDataParser.Parse(data.Payload, thresholds));
                var status = Run(AtaOperation.SmartStatus, "Reading SMART status");
                if (status.Succeeded && SmartReturnStatus.Interpret(status) is null)
                    commands[^1] = status with { Outcome = AtaOutcome.InvalidResponse, Explanation = "Unrecognized SMART RETURN STATUS task-file response; status is Unknown." };
            }
        }
        catch (AtaTransportException ex) { Skip(ex.Outcome, ex.Message); DiscoveryLog.Write("SATA transport stopped", ex); }
        token.ThrowIfCancellationRequested(); progress?.Report("Preparing results");
        var mapped = SataResultMapper.Map(device, identity, attributes, thresholds, commands, notes);
        var assessment = AssessmentPolicies.Assess(mapped);
        return mapped with { Assessment = assessment, Summary = $"Read sequence finished · {commands.Count(c => c.Succeeded)}/{commands.Count} valid command responses · Assessment {assessment.StateDisplay} · Checklist {assessment.ChecklistDisplay}" };

        void Skip(AtaOutcome outcome, string reason)
        {
            foreach (var op in Enum.GetValues<AtaOperation>().Where(op => commands.All(c => c.Operation != op)))
                commands.Add(AtaCommandResult.Missing(op, outcome, reason));
        }
    }
}
