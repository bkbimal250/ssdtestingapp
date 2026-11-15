using pixinit.Core.Devices;
using pixinit.Core.Benchmark;
using pixinit.Core.History;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Core.Diagnostics.Common;
using pixinit.ViewModels.Qc;

namespace pixinit.Tests;
internal static partial class Program
{
    private static void QcTests()
    {
        var qc = new QcDashboard();
        qc.Update(null, null, null, null);
        Check(qc.Rows.Count == 5 && qc.Rows[0].Status == "NOT TESTED" && qc.Rows[1].Status == "NOT TESTED" && qc.Rows[2].Status == "NOT APPLICABLE" && qc.Rows[3].Status == "CAUTION", "QC missing evidence never passes");
        Check(qc.Rows.All(r => !r.Value.Contains("N/A") && r.Reason.Length > 0), "QC unavailable values have reasons");
        var disk = SyntheticNvme();
        var controllerBytes = SyntheticController();
        var healthBytes = SyntheticHealth();
        Array.Clear(healthBytes, 32, 160);
        healthBytes[0] = 0; healthBytes[5] = 6;
        healthBytes[1] = 0x42; healthBytes[2] = 1;
        var now = DateTimeOffset.UtcNow;
        NvmeDiagnostics Reading(byte[] bytes) => NvmeResultMapper.Map(disk, NvmeControllerParser.Parse(controllerBytes), NvmeHealthParser.Parse(bytes), [],
            [new(NvmeOperation.Controller, NvmeOutcome.Success, "Fixture", controllerBytes, now, "Fixture"), new(NvmeOperation.Health, NvmeOutcome.Success, "Fixture", bytes, now, "Fixture")]);
        qc.Update(disk, Reading(healthBytes), null, null);
        Check(qc.Rows[0].Status == "PASS" && qc.Rows[0].Value == "Serial + Firmware verified", "QC identity comes from Identify Controller");
        Check(qc.Rows[2].Status == "NOT APPLICABLE" && qc.Rows[3].Status == "PASS", "QC valid media error counters pass");
        Check(qc.Rows[4].Value.Contains("94%") && qc.Rows[4].Status == "PASS", "QC derives NVMe life from live health data");
        healthBytes[1] = 0x58; healthBytes[2] = 1;
        healthBytes[144] = 101; healthBytes[160] = 1;
        qc.Update(disk, Reading(healthBytes), null, null);
        Check(qc.Rows[2].Status == "NOT APPLICABLE", "QC labels long-running endurance testing as outside PIXINIT");
        Check(qc.Rows[3].Status == "FAIL", "QC nonzero NVMe media errors fail");
        healthBytes[1] = 0x42;
        qc.Update(disk, Reading(healthBytes), null, null);
        Check(qc.Rows[2].Status == "NOT APPLICABLE", "QC keeps the reliability row outside PIXINIT");
        qc.Update(disk with { Id = "another-test-device" }, Reading(healthBytes), null, null);
        Check(qc.Rows[0].Status == "NOT TESTED" && qc.Rows[3].Status == "CAUTION", "QC rejects readings from a different disk");
        qc.Update(disk, Reading(healthBytes), null, null, stale: true);
        Check(qc.Rows[0].Status == "NOT TESTED" && qc.Rows[3].Status == "CAUTION", "QC withholds stale evidence");
        Check(qc.Rows[1].Status == "NOT TESTED" && qc.Rows[1].Value.Contains("Run Benchmark"), "QC performance does not execute writes or invent benchmark values");
        var payload = Convert.FromHexString(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "silicon-motion-256gb.hex")).Trim());
        var attributes = SmartDataParser.Parse(payload, null);
        var sataDisk = Drive("qc-sata-fixture", StorageProtocol.Sata);
        var command = new AtaCommandResult(AtaOperation.SmartData, AtaOutcome.Success, "Fixture", payload, [], now);
        var sata = SataResultMapper.Map(sataDisk, null, attributes, null, [command], []);
        qc.Update(sataDisk, null, sata, null);
        Check(qc.Rows[3].Status == "PASS" && qc.Rows[4].Value.Contains("100%"), "QC verifies SATA zero error counters and recognized A9 life encoding");
        qc.Update(sataDisk, null, sata with { Attributes = sata.Attributes.Where(a => a.Id != 0xC5).ToArray() }, null);
        Check(qc.Rows[3].Status == "CAUTION", "Missing SATA integrity counter is not assumed zero");
        qc.Update(sataDisk, null, sata with { Attributes = sata.Attributes.Select(a => a.Id == 0x05 ? a with { RawBytes = [1,0,0,0,0,0] } : a).ToArray() }, null);
        Check(qc.Rows[3].Status == "FAIL", "SATA reallocated sectors fail QC");
        qc.Update(sataDisk, null, sata with { Commands = [] }, null);
        Check(qc.Rows[3].Status == "CAUTION" && qc.Rows[4].Status == "NOT TESTED", "Failed SATA reads cannot supply apparently live QC data");
        disk = disk with { InterfacePath = "fixture-interface", Serial = "fixture-serial", CapacityBytes = 1073741824 };
        var config = new BenchmarkConfiguration(BenchmarkPreset.Custom, BenchmarkOperation.SequentialRead | BenchmarkOperation.SequentialWrite | BenchmarkOperation.RandomRead | BenchmarkOperation.RandomWrite, 1073741824, 1048576, 4096, 1, 256, 1) { Unbuffered = true };
        BenchmarkOperation[] ops = [BenchmarkOperation.SequentialRead, BenchmarkOperation.SequentialWrite, BenchmarkOperation.RandomRead, BenchmarkOperation.RandomWrite];
        var measurements = ops.Select(op => new BenchmarkOperationResult(op, BenchmarkOperationState.Completed, 1048576, 256, 1, 1048576, 1.048576, 1, 256, new BenchmarkLatency(1,1,1,256), null)).ToArray();
        var target = new BenchmarkTarget("fixture", "fixture", "NTFS", 1073741824, SnapshotFactory.DeviceSnapshot(disk), "Fixture", true);
        var session = new BenchmarkSession(Guid.NewGuid(), now, now, "Fixture", "Fixture", target, config, measurements, BenchmarkCompletion.Completed, null, 0,0,0,0,1,true,true,null,null,null,true);
        qc.Update(disk, null, null, session, benchmarkDone: true, benchmarkResult: "Benchmark done - Seq Read 1000 MB/s");
        Check(qc.Rows[1].Status == "PASS" && qc.Rows[1].Value.Contains("Seq Read 1000"), "QC uses completed benchmark state from the shell");
        qc.Update(disk, null, null, session with { Configuration = config with { FileSizeBytes = 536870912 } });
        Check(qc.Rows[1].Status == "NOT TESTED", "QC does not mark an unfinished benchmark as complete");
        qc.Update(disk, null, null, session with { Configuration = config with { Unbuffered = false } });
        Check(qc.Rows[1].Status == "NOT TESTED", "QC does not mark a buffered benchmark as complete");
        qc.Update(disk with { Serial = "other-fixture" }, null, null, session);
        Check(qc.Rows[1].Status == "NOT TESTED", "QC does not reuse performance after the shared completion state is absent");

    }
}
