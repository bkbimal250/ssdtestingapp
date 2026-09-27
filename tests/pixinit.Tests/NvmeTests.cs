using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using pixinit.Application.Discovery;
using pixinit.Application.Scanning;
using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Infrastructure.Windows.Nvme;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;

internal static partial class Program
{
    private static StorageDevice SyntheticNvme(string id = "nvme-a") => new(id, "SYNTHETIC NVME MODEL", "SYNTHETIC-NVME", StorageProtocol.Nvme, ConnectionBus.Nvme,
        DiagnosticCapabilities.Identity | DiagnosticCapabilities.NvmeHealth, DeviceAccess.Available, true)
    { DiskNumber = 0, InterfacePath = "synthetic-nvme-path", InstanceId = "synthetic-instance", Firmware = "N001", CapacityBytes = 512_000_000, NativeBusType = 17 };
    private static byte[] SyntheticController()
    {
        var b = new byte[4096];
        void W16(int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v);
        void W32(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
        void TextAt(int o, int n, string value) { b.AsSpan(o, n).Fill(32); System.Text.Encoding.ASCII.GetBytes(value).CopyTo(b, o); }
        W16(0, 0x8086); W16(2, 0x8086); TextAt(4, 20, "SYNTHETIC-NVME"); TextAt(24, 40, "SYNTHETIC NVME MODEL"); TextAt(64, 8, "N001"); W16(78, 7); W32(80, 0x10400);
        b[261] = 3; b[262] = 15; W16(266, 350); W16(268, 370); W16(322, 1); W32(516, 2); return b;
    }
    private static byte[] SyntheticNamespace()
    {
        var b = new byte[4096];
        BinaryPrimitives.WriteUInt64LittleEndian(b, 1_000_000); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(8), 900_000); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16), 400_000);
        b[24] = 1; b[25] = 1; b[26] = 1; BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(132), 8); b[134] = 12; return b;
    }
    private static byte[] SyntheticHealth()
    {
        var b = new byte[512]; b[0] = 0xA5; BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1), 300); b[3] = 98; b[4] = 10; b[5] = 123;
        b[32] = 2; b[47] = 1; // > UInt64.MaxValue
        b[48] = 3; b[64] = 4; b[80] = 5; b[96] = 6; b[112] = 7; b[128] = 8; b[144] = 9; b[160] = 10; b[176] = 11;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(192), 12); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(196), 13);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(200), 310); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(202), 0); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(204), 0xFFFF);
        for (int i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(216 + i * 4), (uint)(14 + i)); return b;
    }
    private static byte[] SyntheticErrors()
    { var b = new byte[512]; BinaryPrimitives.WriteUInt64LittleEndian(b, 19); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), 2); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(12), 0x4002); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(16), 33); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24), 1); return b; }

    private static void NvmeParserAndPolicyTests()
    {
        var c = NvmeControllerParser.Parse(SyntheticController());
        Check(c.Model == "SYNTHETIC NVME MODEL" && c.Serial == "SYNTHETIC-NVME" && c.Firmware == "N001", "NVMe controller strings decode without ATA word swapping");
        Check(c.Vendor == 0x8086 && c.Id == 7 && c.Version == 0x10400 && c.Namespaces == 2, "NVMe controller identifiers, version and namespace count parse");
        Check(c.ErrorSlots == 16 && c.WarningKelvin == 350 && c.CriticalKelvin == 370 && c.ThermalManagement, "NVMe controller log bounds and thermal capabilities parse");
        bool invalid = false; try { NvmeControllerParser.Parse(new byte[4096]); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Zero Identify Controller rejected");
        invalid = false; try { NvmeControllerParser.Parse(new byte[100]); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Truncated Identify Controller rejected");
        var corrupt = SyntheticController(); corrupt[24] = 1; invalid = false; try { NvmeControllerParser.Parse(corrupt); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Invalid controller ASCII rejected");
        var ns = NvmeNamespaceParser.Parse(SyntheticNamespace(), 9);
        Check(ns.Id == 9 && ns.BlockBytes == 4096 && ns.SizeBytes == new BigInteger(4_096_000_000), "Namespace scope and 4K byte capacity use wide arithmetic");
        Check(ns.Utilization == 400_000 && ns.MetadataBytes == 8 && ns.Format == 1, "Namespace utilization validity and active format parse");
        invalid = false; try { NvmeNamespaceParser.Parse(SyntheticNamespace(), 0); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Unavailable namespace ID is rejected rather than guessed");
        corrupt = SyntheticNamespace(); corrupt[134] = 63; invalid = false; try { NvmeNamespaceParser.Parse(corrupt, 1); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Namespace capacity shift overflow is rejected");
        var wide = SyntheticNamespace(); BinaryPrimitives.WriteUInt64LittleEndian(wide, ulong.MaxValue); BinaryPrimitives.WriteUInt64LittleEndian(wide.AsSpan(8), ulong.MaxValue); BinaryPrimitives.WriteUInt64LittleEndian(wide.AsSpan(16), ulong.MaxValue); wide[134] = 31;
        Check(NvmeNamespaceParser.Parse(wide, 1).CapacityBytes > ulong.MaxValue, "Namespace byte capacity uses BigInteger beyond UInt64 range");
        var h = NvmeHealthParser.Parse(SyntheticHealth());
        Check(h.Warning == 0xA5 && NvmeHealthParser.Warnings(h.Warning).Contains("0xA0"), "Known and unknown Critical Warning bits are preserved");
        Check(Math.Abs(h.Celsius!.Value - 26.85) < .001 && h.Sensors[0] is > 36.84 and < 36.86 && h.Sensors[1] is null && h.Sensors[2] is null, "Kelvin conversion with unavailable sensors is correct");
        Check(h.Used == 123 && h.Spare == 98 && h.SpareThreshold == 10, "Percentage Used above 100 and spare values are preserved");
        Check(h.Counters[0] > ulong.MaxValue && NvmeHealthParser.DataUnitBytes(h.Counters[1]) == 1_536_000, "Unsigned 128-bit counters and 512000-byte Data Units avoid truncation");
        var zero = NvmeHealthParser.Parse(new byte[512]);
        var zeroMap = NvmeResultMapper.Map(SyntheticNvme(), c, zero, null, [new(NvmeOperation.Health, NvmeOutcome.Success, "Synthetic zero", new byte[512], DateTimeOffset.UtcNow, "Synthetic scope")]);
        Check(zero.Counters.All(v => v == 0) && zero.Celsius is null && zeroMap.UsageCounters!.Contains("bytes: unavailable"), "Legitimate zero counters accepted while zero temperature and unreported Data Units stay unavailable");
        var errors = NvmeErrorParser.Parse(SyntheticErrors(), 8); Check(errors.Count == 1 && errors[0].Count == 19 && errors[0].Lba == 33, "Bounded error log omits unused entries and parses populated entry");
        invalid = false; try { NvmeErrorParser.Parse(new byte[576], 9); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Error log bound rejects more than eight entries");
        foreach (var op in Enum.GetValues<NvmeOperation>()) { uint id = op == NvmeOperation.Namespace ? 3U : 0U; var req = NvmeQueryPolicy.Request(op, id); NvmeQueryPolicy.ValidateRequest(op, req, id); }
        Check(true, "Exact NVMe read-query allowlist accepts four supported request types");
        invalid = false; try { var req = NvmeQueryPolicy.Request(NvmeOperation.Health); req[12] = 3; NvmeQueryPolicy.ValidateRequest(NvmeOperation.Health, req); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Mutated NVMe data type is rejected by exact allowlist");
        invalid = false; try { NvmeQueryPolicy.Request(NvmeOperation.Namespace, 0); } catch (InvalidDataException) { invalid = true; } Check(invalid, "Namespace query cannot use zero or guessed ID");
        var returned = NvmeQueryPolicy.Request(NvmeOperation.Health); BinaryPrimitives.WriteUInt32LittleEndian(returned, 48); BinaryPrimitives.WriteUInt32LittleEndian(returned.AsSpan(4), 48);
        var response = NvmeQueryPolicy.Validate(NvmeOperation.Health, returned, (uint)returned.Length, true, 0); Check(response.Succeeded && response.Payload.Length == 512 && response.Scope.Contains("Windows"), "Valid protocol descriptor extracts bounded device-scope payload");
        var bad = returned.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(24), uint.MaxValue); response = NvmeQueryPolicy.Validate(NvmeOperation.Health, bad, (uint)bad.Length, true, 0); Check(response.Outcome == NvmeOutcome.InvalidResponse, "Overflowing protocol offset rejected");
        bad = returned.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(8), 2); response = NvmeQueryPolicy.Validate(NvmeOperation.Health, bad, (uint)bad.Length, true, 0); Check(response.Outcome == NvmeOutcome.InvalidResponse, "Returned non-NVMe protocol descriptor rejected");
        response = NvmeQueryPolicy.Validate(NvmeOperation.Health, returned, 47, true, 0); Check(response.Outcome == NvmeOutcome.InvalidResponse, "Truncated protocol descriptor rejected");
        response = NvmeQueryPolicy.Validate(NvmeOperation.Health, returned, (uint)returned.Length, false, 5); Check(response.Outcome == NvmeOutcome.AccessDenied, "Windows access denial remains distinct");
        response = NvmeQueryPolicy.Validate(NvmeOperation.Health, returned, (uint)returned.Length, false, 50); Check(response.Outcome == NvmeOutcome.Unsupported, "Windows unsupported result remains distinct");
        Check(!NvmeIdentityGuard.Matches(SyntheticNvme(), SyntheticNvme() with { Serial = "REUSED" }), "Identity mismatch detects disk-number reuse");
    }

    private static async Task NvmeIntegrationTests()
    {
        var transport = new SyntheticNvmeTransport(); var provider = new WindowsNvmeDiagnosticsProvider(transport);
        var result = await provider.ReadAsync(SyntheticNvme(), default);
        Check(transport.Operations.SequenceEqual([NvmeOperation.Controller, NvmeOperation.Health, NvmeOperation.Errors]), "Provider dispatches only allowlisted controller, health and bounded error queries");
        Check(result.Responses.Single(r => r.Operation == NvmeOperation.Namespace).Outcome == NvmeOutcome.NotQueried && result.ControllerDetails!.Contains("No namespace ID guessed"), "Controller results retained while namespace mapping is unavailable");
        Check(result.Responses.Single(r => r.Operation == NvmeOperation.Namespace).Scope.Contains("unavailable"), "Withheld namespace result does not claim an established namespace zero");
        Check(result.PercentageUsed.Value == 123 && result.UsageCounters!.Contains("TiB") && result.UsageCounters.Contains("rounded up"), "Mapped usage preserves endurance consumed and conversion limits");
        Check(result.WarningDetails.Contains("not an overall health guarantee") && result.Assessment is not null && result.Summary.Contains("Assessment"), "NVMe status remains distinct from the separately derived application assessment");
        transport.Fail = NvmeOperation.Errors; result = await provider.ReadAsync(SyntheticNvme(), default); Check(result.Temperature.Availability == Core.Diagnostics.Common.Availability.Available && result.ErrorDetails!.Contains("Unsupported"), "Optional error-log failure retains successful health");
        bool rejected = false; try { await new NvmeOperationCoordinator(provider, new StorageOperationGate()).ReadAsync(SyntheticSata(), default); } catch (InvalidOperationException) { rejected = true; } Check(rejected && transport.Operations.Count == 6, "SATA device rejected before NVMe transport dispatch");

        var gate = new StorageOperationGate(); var discovery = new ControlledDiscovery(); var blocked = new BlockingNvmeProvider();
        var shell = new ShellViewModel(new DiscoveryCoordinator(discovery, gate), null, new NvmeOperationCoordinator(blocked, gate));
        var scan = shell.ScanAsync(); discovery.Completion.SetResult([SyntheticNvme(), SyntheticNvme("nvme-b")]); await scan;
        var read = shell.ReadNvmeAsync(); await blocked.Started.Task;
        Check(!shell.ScanCommand.CanExecute(null) && !shell.ReadNvmeCommand.CanExecute(null), "NVMe diagnostics exclude rescans and duplicate reads while native work is active");
        Check(!shell.Benchmark.StartCommand.CanExecute(null), "Benchmark cannot start while NVMe diagnostics are in flight");
        shell.SelectedNvme = shell.NvmeDevices.Single(d => d.Id == "nvme-b"); blocked.Completion.SetResult(result with { DeviceId = "nvme-a" }); await read;
        Check(shell.Nvme.Result is null && shell.SelectedNvme.Id == "nvme-b" && shell.NvmeOperationStatus.Contains("cancelled"), "NVMe selection change cancels and rejects stale result");
        blocked = new(); shell = new(new DiscoveryCoordinator(discovery, gate), null, new NvmeOperationCoordinator(blocked, gate)); await shell.ScanAsync(); read = shell.ReadNvmeAsync(); await blocked.Started.Task; shell.Cancel();
        Check(shell.NvmeBusy && !shell.ScanCommand.CanExecute(null), "Cancelled NVMe native worker retains operation gate until completion"); blocked.Completion.SetResult(result); await read;
        Check(shell.Nvme.Result is null && shell.NvmeOperationStatus.Contains("Partial data discarded"), "Cancelled NVMe partial result is not published as complete");
        blocked = new(); shell = new(new DiscoveryCoordinator(discovery, gate), null, new NvmeOperationCoordinator(blocked, gate)); await shell.ScanAsync(); read = shell.ReadNvmeAsync(); await blocked.Started.Task; shell.Close(); blocked.Completion.SetResult(result); await read;
        Check(shell.Nvme.Result is null && !shell.ReadNvmeCommand.CanExecute(null), "Application close rejects late NVMe result");
    }
    private sealed class SyntheticNvmeTransport : INvmeTransport
    {
        public NvmeOperation? Fail; public List<NvmeOperation> Operations { get; } = [];
        public NvmeResponse Query(StorageDevice device, NvmeOperation operation, CancellationToken token, uint namespaceId = 0)
        { Operations.Add(operation); var payload = operation switch { NvmeOperation.Controller => SyntheticController(), NvmeOperation.Health => SyntheticHealth(), NvmeOperation.Errors => SyntheticErrors(), _ => SyntheticNamespace() };
          return new(operation, operation == Fail ? NvmeOutcome.Unsupported : NvmeOutcome.Success, operation == Fail ? "Synthetic unsupported" : "Synthetic success", payload, DateTimeOffset.UtcNow, NvmeQueryPolicy.Scope(operation, namespaceId)); }
    }
    private sealed class BlockingNvmeProvider : INvmeDiagnosticsProvider
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource<NvmeDiagnostics> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<NvmeDiagnostics> ReadAsync(StorageDevice device, CancellationToken token, IProgress<string>? progress = null) { Started.SetResult(); return Completion.Task; }
    }
}
