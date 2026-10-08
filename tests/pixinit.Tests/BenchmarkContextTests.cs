using System.IO;
using pixinit.Application.Scanning;
using pixinit.Core.Benchmark;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;
using pixinit.ViewModels.Benchmark;

namespace pixinit.Tests;
internal static partial class Program
{
    private static async Task<bool> VerifyDiagnosticPreparationAsync(pixinit.Infrastructure.Benchmarking.FileBenchmarkEngine engine, string root)
    {
        var gate = new StorageOperationGate();
        var selected = Drive("diagnostic-context", StorageProtocol.Nvme) with { MountPoints = [Path.GetPathRoot(root)!], InterfacePath = "test-interface", InstanceId = "test-instance" };
        TbwInfo? tbw = null; ThermalInfo? thermal = null; int calls = 0; bool opened = false;
        BenchmarkViewModel? vm = null;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm = new(engine, null, () => [selected], () => selected, () => { }, new Consent(true), operationGate: gate,
            tbw: () => tbw, thermal: () => thermal, hasDiagnosticSnapshot: () => tbw is not null,
            readDiagnostics: async token =>
            {
                calls++;
                using var lease = gate.Enter();
                if (vm!.Busy || !vm.PreparingDiagnostics || vm.StartCommand.CanExecute(null)) throw new InvalidOperationException("Preparation must await diagnostics without a nested benchmark lease");
                await release.Task.WaitAsync(token);
                tbw = new(12); thermal = new(null, null, ThermalState.Green, 35, "Test diagnostic snapshot", DateTimeOffset.UtcNow);
            }, showDiagnostics: () => opened = true)
        { TargetDirectory = root, SequentialRead = false, SequentialWrite = false, RandomRead = false, RandomWrite = false, SustainedWrite = true, SustainedBudgetMiB = 1, SustainedDurationSeconds = 1 };
        vm.SustainedSamples.Add(new(1, 1) { Bytes = 1000000, IntervalSeconds = 1 });
        if (vm.SustainedWriteMBs != 1 || !vm.SustainedOverallDisplay.Contains("Overall: 1.0 MB/s")) throw new InvalidOperationException("Existing interval samples must show a provisional overall average");
        vm.SustainedSamples.Clear();
        var pending = vm.RunAsync();
        if (pending.IsCompleted || !vm.PreparingDiagnostics) throw new InvalidOperationException("Diagnostic preparation must be asynchronous");
        release.SetResult(); await pending;
        if (calls != 1 || vm.Result?.Tbw?.WrittenTB != 12 || vm.Result.Thermal?.ObservedC != 35 || vm.Result.SustainedOverallMBs is not > 0 || vm.DiagnosticsMissing)
            throw new InvalidOperationException("Fresh diagnostic context and overall sustained speed must reach the completed session");
        // Reuse the snapshot with the gate occupied to prevent another filesystem write.
        using (gate.Enter()) await vm.RunAsync();
        if (calls != 1) throw new InvalidOperationException("Existing context must not trigger duplicate diagnostic reads");
        tbw = null; thermal = null; release = new(TaskCreationOptions.RunContinuationsAsynchronously); vm.ConsentProvider = new Consent(true);
        pending = vm.RunAsync(); vm.Cancel(); await pending;
        if (vm.Busy || vm.PreparingDiagnostics || !vm.Status.Contains("preparation cancelled")) throw new InvalidOperationException("Cancelling diagnostic preparation must prevent benchmark writes");
        bool commandRefreshed = false;
        vm.RunDiagnosticsNowCommand.CanExecuteChanged += (_, _) => commandRefreshed = true;
        vm.RefreshDiagnosticContext();
        if (!commandRefreshed || !vm.RunDiagnosticsNowCommand.CanExecute(null)) throw new InvalidOperationException("Missing-data navigation must refresh command availability after selection/context changes");
        vm.RunDiagnosticsNowCommand.Execute(null);
        if (!opened) throw new InvalidOperationException("Missing-data command must navigate to diagnostics");
        return true;
    }
}
