using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Application.Discovery;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;

internal static partial class Program
{
    private static void SataHostCounterTests()
    {
        foreach (var fixture in new[] {
            (File: "silicon-motion-evm128gb.hex", Model: "EVM25/128GB", Reads: 149, Writes: 289, RawRead: 0x12BCUL, RawWrite: 0x2431UL),
            (File: "silicon-motion-256gb.hex", Model: "2.5 SATA SSD 256GB", Reads: 147, Writes: 218, RawRead: 0x1262UL, RawWrite: 0x1B46UL) })
        {
            var payload = Convert.FromHexString(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture.File)).Trim());
            var attributes = SmartDataParser.Parse(payload, null);
            var command = new AtaCommandResult(AtaOperation.SmartData, AtaOutcome.Success, "Imported SMART fixture", payload, [], DateTimeOffset.UtcNow);
            var disk = Drive("counter-fixture", StorageProtocol.Sata) with { Model = fixture.Model };
            var mapped = SataResultMapper.Map(disk, null, attributes, null, [command], []);
            Check(mapped.Tbw?.WrittenTB == fixture.RawWrite * 33554432d / 1e12 && mapped.HostReadsTB == fixture.RawRead * 33554432d / 1e12,
                $"{fixture.Model}: actual SMART payload decodes 32 MiB lifetime counters without rounding loss");
            Check((int)(mapped.Tbw!.WrittenTB!.Value * 1e12 / 1073741824d) == fixture.Writes &&
                  (int)(mapped.HostReadsTB!.Value * 1e12 / 1073741824d) == fixture.Reads,
                $"{fixture.Model}: reproduces CrystalDiskInfo integer totals ({fixture.Reads} read / {fixture.Writes} written)");
            var discovery = new ControlledDiscovery(); discovery.Completion.SetResult([disk]);
            var shell = new ShellViewModel(new DiscoveryCoordinator(discovery)); shell.ScanAsync().GetAwaiter().GetResult();
            shell.Sata.Apply(mapped);
            Check(shell.Sata.EntireSsdInfo.Any(r => r.Label == "Host reads") && shell.Sata.EntireSsdInfo.Any(r => r.Label == "Host writes") &&
                  !shell.Sata.EntireSsdInfo.Any(r => r.Label == "Critical warning" || r.Label == "Available spare"),
                "Decoded SATA host totals return without NVMe-only fields");
            Check(shell.Benchmark.FriendlyHostWrites.Contains("GiB") && !shell.Benchmark.FriendlyHostWrites.Contains("unavailable") &&
                  !shell.FooterWrites.Contains("0.000"), "Mapped host writes reach benchmark card and footer");
            shell.Close();

            var zero = attributes with { Entries = attributes.Entries.Select(r => r.Id is 241 or 242 ? r with { RawBytes = new byte[6] } : r).ToArray() };
            Check(SataHostCounters.Decode(zero, true) is { ReadsTB: 0, WritesTB: 0 }, "Supported zero counters remain valid");
            Check(SataHostCounters.Decode(attributes, false) is null, "Failed SMART reads cannot publish totals");
            var duplicate = attributes with { Entries = attributes.Entries.Append(attributes.Entries.Last()).ToArray() };
            Check(SataHostCounters.Decode(duplicate, true) is null, "Ambiguous duplicate attributes cannot select a counter profile");
            var missingRead = attributes with { Entries = attributes.Entries.Where(r => r.Id != 242).ToArray() };
            Check(SataHostCounters.Decode(missingRead, true) is { ReadsTB: null, WritesTB: not null }, "Missing read counter does not hide valid write total");
            var bad = attributes with { Entries = attributes.Entries.Select(r => r.Id == 241 ? r with { RawBytes = [255, 255, 255, 255, 255, 255] } : r).ToArray() };
            Check(SataHostCounters.Decode(bad, true) is { WritesTB: null, ReadsTB: not null }, "Invalid counter is withheld independently");
        }
    }
}
