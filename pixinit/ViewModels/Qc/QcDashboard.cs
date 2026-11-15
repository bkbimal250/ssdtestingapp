using System.Numerics;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Core.Benchmark;
using pixinit.ViewModels.Shared;

namespace pixinit.ViewModels.Qc;

public sealed record QcRow(string Category, string Status, string Value, string Reason);

// Presentation only: no transport, benchmark execution, or media-write capability.
public sealed class QcDashboard : ObservableObject
{
    public IReadOnlyList<QcRow> Rows { get; private set; } = [];
    public const string PerformanceNote = "New SSD peak speed. Steady State speed can be 10-20% lower after GC / wear-leveling - Kingston SNIA method.";
    private const string Missing = "Unavailable: no validated reading; refresh diagnostics and check controller support.";
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Missing : value;
    private static string Number(object? value) => value is double d ? d.ToString("F1") : value?.ToString() ?? Missing;
    private static ulong? Raw(SataDiagnostics? s, byte id)
    {
        var rows = s?.Attributes.Where(a => a.Id == id).ToArray();
        if (rows is not { Length: 1 } || rows[0].RawBytes.Length != 6 || rows[0].RawBytes.All(b => b == 255)) return null;
        ulong value = 0;
        for (int i = 0; i < 6; i++) value |= (ulong)rows[0].RawBytes[i] << (i * 8);
        return value;
    }
    public void Update(StorageDevice? disk, NvmeDiagnostics? nvme, SataDiagnostics? sata, BenchmarkSession? benchmark, bool stale = false, bool benchmarkDone = false, string? benchmarkResult = null)
    {
        nvme = !stale && disk?.Protocol == StorageProtocol.Nvme && nvme?.DeviceId == disk.Id ? nvme : null;
        sata = !stale && disk?.Protocol == StorageProtocol.Sata && sata?.DeviceId == disk.Id ? sata : null;
        var controller = nvme?.Responses.Any(r => r.Operation == NvmeOperation.Controller && r.Succeeded) == true ? nvme.Controller : null;
        var identity = sata?.Commands.Any(r => r.Operation == AtaOperation.Identify && r.Succeeded) == true ? sata.Identity : null;
        var health = nvme?.Responses.Any(r => r.Operation == NvmeOperation.Health && r.Succeeded) == true ? nvme.Health : null;
        var smart = sata?.Commands.Any(r => r.Operation == AtaOperation.SmartData && r.Succeeded) == true ? sata : null;
        var model = controller?.Model ?? identity?.Model;
        string modelDisplay = !string.IsNullOrWhiteSpace(model) ? model : identity?.Limitations.Any(n => n.Contains("identity string", StringComparison.OrdinalIgnoreCase)) == true
            ? "Unavailable: ATA Identify returned a non-ASCII identity string; the parser withheld it. The USB enclosure name is not the SSD model."
            : Missing;
        var serial = controller?.Serial ?? identity?.Serial;
        var firmware = controller?.Firmware ?? identity?.Firmware;
        var capacity = identity?.CapacityBytes ?? (!stale ? disk?.CapacityBytes : null);
        var rows = new List<QcRow>();
        rows.Add(new("Identification", !string.IsNullOrWhiteSpace(serial) && !string.IsNullOrWhiteSpace(firmware) && capacity > 0 ? "PASS" : "NOT TESTED",
            "Serial + Firmware verified",
            "Serial and firmware require a successful Identify. Capacity uses ATA Identify or live Windows disk capacity; controller is the live Windows controller map."));
        rows.Add(benchmarkDone ? new("Performance", "PASS", benchmarkResult ?? "Benchmark done", "Completed benchmark for the selected disk.") : new("Performance", "NOT TESTED", "Run Benchmark to measure", "Benchmark has not been run in this session."));
        rows.Add(new("Reliability", "NOT APPLICABLE", "Extended endurance testing is in a separate project, not PIXINIT", "PIXINIT does not perform long-running endurance writes."));
        BigInteger?[] errors = disk?.Protocol == StorageProtocol.Nvme
            ? [health?.Counters.Count > 8 ? health.Counters[8] : null]
            : [AsBig(Raw(smart, 0xC5)), AsBig(Raw(smart, 0xC6)), AsBig(Raw(smart, 0x05))];
        string integrity = errors.Any(e => e > 0) ? "FAIL" : errors.All(e => e == 0) ? "PASS" : "CAUTION";
        rows.Add(new("Data Integrity", integrity,
            integrity == "PASS" ? "No media errors" : integrity == "FAIL" ? "Media errors reported" : "Error counters unavailable",
            integrity == "PASS" ? "No Media Errors. SMART counters only; this does not verify every stored byte." : integrity == "FAIL" ? "Nonzero media error counter reported by the device." : "Required SMART counters missing or unreadable; refresh diagnostics. No full-capacity write or media scan performed."));
        var counters = SataHostCounters.Decode(smart is null ? null : new SmartAttributes(smart.Attributes, []), smart is not null);
        int? life = health is not null ? Math.Clamp(100 - (int)health.Used, 0, 100) : counters is not null ? smart?.Attributes.SingleOrDefault(a => a.Id == 0xA9)?.Current : null;
        if (life is < 0 or > 100) life = null;
        double? reads = health?.Counters.Count > 1 && health.Counters[0] > 0 ? (double)NvmeHealthParser.DataUnitBytes(health.Counters[0]) / 1e12 : counters?.ReadsTB;
        double? writes = health?.Counters.Count > 1 && health.Counters[1] > 0 ? (double)NvmeHealthParser.DataUnitBytes(health.Counters[1]) / 1e12 : counters?.WritesTB;
        rows.Add(new("Health & Endurance", health?.Warning > 0 || life == 0 ? "FAIL" : life is not null && integrity == "PASS" ? "PASS" : "NOT TESTED",
            life is not null && integrity == "PASS" ? $"Health {life}%, no errors" : "Health data not available",
            "NVMe bytes = Data Units x 512 x 1000; SATA F1/F2 use 32 MiB only for the verified controller layout. Rated TBW is not reported by standard SMART. WAF > 3 is High Write Amplification; unavailable WAF prevents a full PASS."));
        Rows = rows;
        Changed(nameof(Rows));
    }
    private static BigInteger? AsBig(ulong? value) => value is ulong v ? new BigInteger(v) : null;
    private static string Terabytes(double? value) => value is double v ? $"{v:F6} TB ({v * 1e12 / 1073741824:F2} GiB)" : "Unavailable: host counter not reported or units unverified.";
    private static QcRow Performance(StorageDevice? disk, BenchmarkSession? session, bool stale)
    {
        var target = session?.Target.Device;
        bool same = disk is not null && target is not null && target.InterfacePath == disk.InterfacePath && !string.IsNullOrWhiteSpace(disk.InterfacePath) && target.CapacityBytes == disk.CapacityBytes && target.Serial == disk.Serial;
        if (stale || !same || session is null || !session.IsValidForDeviceAttribution || session.Completion != BenchmarkCompletion.Completed || !session.Configuration.Unbuffered || session.Configuration.FileSizeBytes != 1073741824 || session.Configuration.RandomBlockBytes != 4096 || (session.Configuration.Operations & BenchmarkOperation.SustainedWrite) != 0)
            return new("Performance", "NOT TESTED", "Run Benchmark to measure", "QC never starts a benchmark. Existing benchmark results appear here after an explicitly requested test. No WIPC/WDPC. " + PerformanceNote + " This is a general estimate, not a measured steady-state result or a guarantee for this disk.");
        BenchmarkOperation[] operations = [BenchmarkOperation.SequentialRead, BenchmarkOperation.SequentialWrite, BenchmarkOperation.RandomRead, BenchmarkOperation.RandomWrite];
        var measurements = session.PassResults.Count > 0 ? session.PassResults.Where(p => p.IsValidForDeviceAttribution && p.IncludeInAverage).Select(p => p.Measurement).ToArray() : session.Results;
        bool complete = true;
        var values = new List<string>();
        foreach (var op in operations)
        {
            var items = measurements.Where(m => m.Operation == op && m.State == BenchmarkOperationState.Completed && m.BytesProcessed > 0 && m.MeasuredSeconds > 0).ToArray();
            if (items.Length == 0) { complete = false; values.Add($"{op}: Unavailable: operation not completed."); continue; }
            var latency = items.Where(m => m.Latency is { Samples: > 0 }).Select(m => m.Latency!).ToArray();
            if (latency.Length != items.Length) complete = false;
            values.Add($"{op}: {items.Average(m => m.MegabytesPerSecond):F2} MB/s | IOPS: {items.Sum(m => (double)m.CompletedOperations) / items.Sum(m => m.MeasuredSeconds):F0} | Average latency: {(latency.Length == items.Length ? $"{latency.Sum(l => l.AverageMilliseconds * l.Samples) / latency.Sum(l => (double)l.Samples):F3} ms" : "Unavailable: benchmark did not capture latency.")}");
        }
        return new("Performance", complete ? "PASS" : "CAUTION", string.Join("\n", values), $"Observed {session.FinishedUtc:u}. PASS means measurements available, not a manufacturer speed certification. " + PerformanceNote + " General estimate only; no steady-state test performed.");
    }
}
