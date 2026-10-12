using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using pixinit.Application.Discovery;
using pixinit.Application.Selection;
using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.Infrastructure.Windows.Interop;
using pixinit.ViewModels.Shell;

namespace pixinit.Tests;

internal static partial class Program
{
    private static byte[] Descriptor(uint bus = 11, string serial = "DUPLICATE")
    {
        var bytes = new byte[128];
        Put(bytes, 0, 40); Put(bytes, 4, 128); Put(bytes, 16, 40); Put(bytes, 24, 80); Put(bytes, 28, bus);
        Encoding.ASCII.GetBytes("Test model").CopyTo(bytes, 40); Encoding.ASCII.GetBytes(serial).CopyTo(bytes, 80);
        return bytes;
    }
    private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static bool Invalid(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    private static void DiscoveryTests()
    {
        byte[] ProtocolResponse(uint protocol, byte[] payload)
        {
            var b = new byte[48 + payload.Length]; Put(b, 0, 48); Put(b, 4, 48); Put(b, 8, protocol); Put(b, 12, 1); Put(b, 24, 40); Put(b, 28, (uint)payload.Length); payload.CopyTo(b, 48); return b;
        }
        var raid = Drive("raid", StorageProtocol.Unknown) with { Bus = ConnectionBus.Raid, NativeBusType = 8, IncursSeekPenalty = false, Serial = "SYNTHETIC-NVME" };
        int probes = 0;
        var nvmeRaid = StorageQueryService.ProbeRaid(raid, (_, _, _) => { probes++; return ProtocolResponse(3, SyntheticController()); }, default);
        Check(probes == 1 && nvmeRaid.CanQueryNvme && nvmeRaid.Bus == ConnectionBus.Raid && nvmeRaid.RaidIdentifyRaw.Length == 4096, "RAID no-seek device requires validated attributed NVMe Identify and preserves reported bus/raw payload");
        var ata = SyntheticIdentify(); Word(ata, 217, 1); Checksum(ata);
        StorageDevice ProbeSata(string id) => StorageQueryService.ProbeRaid(raid with { Id = id, Serial = "SYNTHETIC-SERIAL" }, (_, request, _) => {
            if (BinaryPrimitives.ReadUInt32LittleEndian(request!.AsSpan(8)) == 3) throw new Win32Exception(50);
            return ProtocolResponse(2, ata);
        }, default);
        var sataRaid = ProbeSata("raid-sata1");
        Check(sataRaid.CanQuerySata && sataRaid.Media == StorageMedia.Ssd && sataRaid.Bus == ConnectionBus.Raid, "RAID ATA Identify establishes SATA and SSD only from rotation word one");
        var mixedRaid = new[] { nvmeRaid, sataRaid, ProbeSata("raid-sata2") };
        Check(mixedRaid.Count(d => d.Protocol == StorageProtocol.Nvme) == 1 && mixedRaid.Count(d => d.Protocol == StorageProtocol.Sata) == 2 && mixedRaid.All(d => d.Protocol != StorageProtocol.Unknown), "Validated simulated RAID inventory has one NVMe and two SATA devices");
        var failedRaid = StorageQueryService.ProbeRaid(raid, (_, _, _) => throw new Win32Exception(5), default);
        Check(failedRaid.Protocol == StorageProtocol.Unknown && failedRaid.Access == DeviceAccess.AccessDenied && failedRaid.Limitations.Contains("administrator"), "Denied RAID probes retain unknown protocol with actionable access message");
        var mismatchRaid = StorageQueryService.ProbeRaid(raid with { Serial = "DIFFERENT" }, (_, _, _) => ProtocolResponse(3, SyntheticController()), default);
        Check(mismatchRaid.Protocol == StorageProtocol.Unknown && !mismatchRaid.RaidProtocolVerified, "RAID controller identity mismatch cannot classify a selected disk");
        var malformedRaid = StorageQueryService.ProbeRaid(raid, (_, _, _) => new byte[3], default);
        Check(malformedRaid.Protocol == StorageProtocol.Unknown, "Malformed RAID Identify responses remain unidentified");
        var heuristic = RaidDisplayFallback.Apply([raid with { Model = "NVMe 512GB SSD", Access = DeviceAccess.Limited, CapacityBytes = 512000000000, DiskNumber = 2 }], [new("SYNTHETIC-NVME", 512000000000, 8, 4, "2")]).Single();
        Check(heuristic.Protocol == StorageProtocol.Nvme && heuristic.ProtocolHeuristic && !heuristic.CanQueryNvme && heuristic.Media == StorageMedia.Ssd, "RAID model/MSFT fallback displays NVMe SSD without authorizing transport after invalid Identify");
        var heuristicSata = RaidDisplayFallback.Apply([raid with { Model = "2.5 SATA SSD 256GB", Access = DeviceAccess.Limited }], []).Single();
        Check(heuristicSata.Protocol == StorageProtocol.Sata && !heuristicSata.CanQuerySata && heuristicSata.MediaInferred, "RAID SATA model fallback is explicitly inferred and cannot authorize ATA commands");
        Check(WindowsDiskDiscovery.Deduplicate([raid with { Serial = "SAME", CapacityBytes = 100, DiskNumber = 0 }, raid with { Id = "different", Serial = "SAME", CapacityBytes = 100, DiskNumber = 1 }]).Count == 2, "Distinct physical disk numbers with duplicated controller serial remain separate");
        var clientDisks = new[] {
            sataRaid with { Id = "evm", DiskNumber = 1, Model = "EVM25/128GB", Serial = "ES1HNO925134418", CapacityBytes = 128_035_676_160 },
            raid with { Id = "sata256", DiskNumber = 0, Model = "2.5 SATA SSD 256GB", Serial = "SATA256", CapacityBytes = 256_060_514_304, Access = DeviceAccess.Limited },
            raid with { Id = "nvme512", DiskNumber = 2, Model = "NVMe 512GB SSD", Serial = "NVME512", CapacityBytes = 512_110_190_592, Access = DeviceAccess.Limited }
        };
        var clientInventory = RaidDisplayFallback.Apply(WindowsDiskDiscovery.Deduplicate([.. clientDisks, clientDisks[1] with { Id = "duplicate-interface" }]), [
            new("SATA256", 256_060_514_304, 8, 4, "0", "2.5 SATA SSD 256GB"),
            new("NVME512", 512_110_190_592, 8, 4, "2", "NVMe 512GB SSD")]);
        Check(clientInventory.Count == 3 && clientInventory.Count(d => d.Protocol == StorageProtocol.Sata) == 2 && clientInventory.Count(d => d.Protocol == StorageProtocol.Nvme) == 1, "Client-shaped RAID simulation deduplicates and routes all three disks without fabricated readings");
        Check(clientInventory.Where(d => d.ProtocolHeuristic).All(d => d.Access == DeviceAccess.Limited && !d.CanQuerySata && !d.CanQueryNvme && d.ProtocolBadge.Contains("Heuristic") && d.Media == StorageMedia.Ssd), "Windows Storage SSD reports permit navigation but never authorize heuristic SMART access");
        Check(clientInventory[0].CanQuerySata && clientInventory[0].Serial == "ES1HNO925134418", "Validated EVM identity remains queryable with full serial retained, without an authenticity claim");
        var genericRaid = raid with { Model = "Any brand SSD", DiskNumber = 4, CapacityBytes = 1_000_000_000, Serial = "TEST-ONLY", Access = DeviceAccess.Limited };
        var unknownSsd = RaidDisplayFallback.Apply([genericRaid], [new("TEST-ONLY", 1_000_000_000, 8, 4, "4")]).Single();
        Check(unknownSsd.Protocol == StorageProtocol.Unknown && unknownSsd.Media == StorageMedia.Ssd && unknownSsd.HasStorageOverview, "Generic SSD media does not imply SATA or NVMe; physical overview remains available");
        var unknownHdd = RaidDisplayFallback.Apply([genericRaid with { Model = "Any brand", IncursSeekPenalty = true }], [new("TEST-ONLY", 1_000_000_000, 8, 3, "4")]).Single();
        Check(unknownHdd.Media == StorageMedia.Hdd && unknownHdd.MediaDisplay == "HDD (reported)", "Windows Storage HDD media retained independently of protocol");
        Check(!(genericRaid with { Bus = ConnectionBus.Virtual }).HasStorageOverview && !(genericRaid with { CapacityBytes = null }).HasStorageOverview, "Virtual and unobserved-capacity entries remain additional devices");
        Check(Marshal.SizeOf<StorageNative.InterfaceData>() == 32 && Marshal.SizeOf<StorageNative.DeviceInfo>() == 32 && IntPtr.Size == 8,
            "SetupAPI fixed structures match validated x64 ABI");
        Check(DescriptorParser.Classify(11).Protocol == StorageProtocol.Sata && DescriptorParser.Classify(17).Protocol == StorageProtocol.Nvme, "Direct Windows SATA/NVMe bus classification");
        Check(new uint[] { 0, 1, 3, 7, 8, 10, 14, 15, 16, 999 }.All(b => DescriptorParser.Classify(b).Protocol == StorageProtocol.Unknown), "ATA, USB, RAID, SCSI, SAS, virtual, Spaces and unknown buses remain unidentified");
        Check(Invalid(() => DescriptorParser.ParseDevice(new byte[3])) && Invalid(() => DescriptorParser.ParseDevice(Descriptor()[..50])), "Truncated descriptor/header rejected");
        var bad = Descriptor(); Put(bad, 24, uint.MaxValue);
        var partial = DescriptorParser.ParseDevice(bad);
        Check(partial.Serial is null && partial.Model == "Test model" && partial.Warnings.Length > 0, "Invalid serial offset preserves other descriptor fields");
        bad = Descriptor(); Array.Fill(bad, (byte)'X', 80, 48);
        Check(DescriptorParser.ParseDevice(bad).Serial is null, "Unterminated descriptor string rejected within bounds");
        bad = Descriptor(); Put(bad, 4, uint.MaxValue);
        Check(Invalid(() => DescriptorParser.ParseDevice(bad)), "Oversized descriptor allocation rejected");
        bad = Descriptor(); Put(bad, 32, uint.MaxValue);
        Check(Invalid(() => DescriptorParser.ParseDevice(bad)), "Raw property length checked");
        Check(Invalid(() => WindowsDiskDiscovery.ReadWidePath([65, 0])) && WindowsDiskDiscovery.ReadWidePath([65, 0, 0, 0]) == "A", "Interface paths require bounded UTF-16 terminator");
        byte[] extents = new byte[56]; Put(extents, 0, 2); Put(extents, 8, 1); Put(extents, 32, 1);
        Check(DescriptorParser.ParseExtents(extents).SequenceEqual(new uint[] { 1 }), "Multiple extents on one physical disk can map unambiguously");
        Put(extents, 32, 2);
        Check(DescriptorParser.ParseExtents(extents).Length == 2 && Invalid(() => DescriptorParser.ParseExtents(extents[..32])), "Multiple disks distinguished; truncated extent list rejected");

        byte[] Query(uint code, byte[]? input, int size)
        {
            if (code == StorageNative.QueryProperty && DescriptorParser.U32(input!, 0) == 0) return Descriptor()[..Math.Min(size, 128)];
            if (code == StorageNative.DeviceNumber) { var bytes = new byte[12]; Put(bytes, 0, 7); Put(bytes, 4, 2); return bytes; }
            if (code == StorageNative.GeometryEx) { var bytes = new byte[32]; BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(24), 500_000_000_000); return bytes; }
            throw new Win32Exception(50);
        }
        var first = WindowsDiskDiscovery.ReadDevice("path1", "instance1", default, Query);
        var second = WindowsDiskDiscovery.ReadDevice("path2", "instance2", default, Query);
        Check(first.Protocol == StorageProtocol.Sata && first.Media == StorageMedia.Unknown && first.Capabilities == DiagnosticCapabilities.Identity, "SATA does not imply SSD or diagnostic support");
        byte[] SeekQuery(uint code, byte[]? input, int size)
        {
            if (code == StorageNative.QueryProperty && input is not null && BinaryPrimitives.ReadUInt32LittleEndian(input) == 7)
            { var bytes = new byte[12]; Put(bytes, 0, 12); Put(bytes, 4, 12); return bytes; }
            return Query(code, input, size);
        }
        var seekDevice = WindowsDiskDiscovery.ReadDevice("seek", null, default, SeekQuery);
        Check(seekDevice.IncursSeekPenalty == false && seekDevice.Media == StorageMedia.Unknown, "Seek evidence is retained without inventing SSD classification");
        var mediaVm = new pixinit.ViewModels.Nvme.NvmeViewModel();
        mediaVm.SetDevice(seekDevice);
        Check(mediaVm.MediaDisplay == "Not confirmed" && mediaVm.MediaEvidence.Contains("no seek penalty") && !seekDevice.Limitations.Contains("not yet queried"), "Media uncertainty explains evidence without stale diagnostic wording");
        mediaVm.SetDevice(null);
        Check(mediaVm.MediaEvidence.StartsWith("Select a drive"), "Media evidence clears on deselection");
        byte[] BadCapacity(uint code, byte[]? input, int size) => code == StorageNative.GeometryEx ? [1, 2] : Query(code, input, size);
        var incomplete = WindowsDiskDiscovery.ReadDevice("incomplete", null, default, BadCapacity);
        Check(incomplete.Protocol == StorageProtocol.Sata && incomplete.CapacityBytes is null && incomplete.Model == first.Model && incomplete.Limitations.Contains("capacity"), "Invalid capacity preserves a classified device with other real identity fields");
        Check(first.CapacityBytes == 500_000_000_000 && first.DiskNumber == 2 && first.Firmware is null, "Real query fields retained when optional firmware/media unavailable");
        Check(WindowsDiskDiscovery.Deduplicate([first, second, first]).Count == 1, "Duplicate physical-number/serial/capacity records merged");
        Check(WindowsDiskDiscovery.Deduplicate([first with { Serial = null }, second with { Serial = null }]).Count == 2, "Missing serials preserve distinct identities");
        var conflict = WindowsDiskDiscovery.Deduplicate([first, first with { Bus = ConnectionBus.Nvme, Protocol = StorageProtocol.Nvme }]).Single();
        Check(conflict.Protocol == StorageProtocol.Unknown && conflict.Limitations.Contains("Conflicting"), "Conflicting duplicate bus evidence withholds classification");
        var denied = WindowsDiskDiscovery.ReadDevice("denied", null, default, (_, _, _) => throw new Win32Exception(5));
        Check(denied.Access == DeviceAccess.AccessDenied && denied.Protocol == StorageProtocol.Unknown && denied.CapacityBytes is null, "Per-device access denial retains a partial record");
        Check(WindowsDiskDiscovery.Deduplicate([first, denied]).Count == 2, "Failed device does not remove successful device");
        var mapping = WindowsDiskDiscovery.ApplySystemMapping([first], [2], null);
        Check(mapping[0].IsUnambiguousSystemDisk, "Single direct disk backing Windows preferred");
        Check(!WindowsDiskDiscovery.ApplySystemMapping([first, second], [2], null).Any(d => d.IsUnambiguousSystemDisk) &&
            !WindowsDiskDiscovery.ApplySystemMapping([first], [2, 3], null).Any(d => d.IsUnambiguousSystemDisk), "Duplicate disk-number matches and multi-disk Windows volumes remain ambiguous");
        Check(!WindowsDiskDiscovery.ApplySystemMapping([denied with { DiskNumber = 2 }], [2], null)[0].IsUnambiguousSystemDisk, "Virtual/unknown underlying storage never receives physical system preference");
        var state = new DeviceSelection(); state.Apply([first, second], state.BeginDiscovery()); state.SelectDevice(StorageProtocol.Sata, first.Id);
        state.Apply([first with { DiskNumber = 9 }, second], state.BeginDiscovery());
        Check(state.SataId == first.Id, "Changed disk number does not change session identity selection");
        state.SelectTab(StorageProtocol.Nvme); state.Apply([first], state.BeginDiscovery());
        Check(state.ActiveProtocol == StorageProtocol.Nvme, "Explicit empty-tab inspection survives subsequent rescans");
        state.Apply([], state.BeginDiscovery()); Check(state.SataId is null && state.NvmeId is null, "Successful empty enumeration clears disconnected selections");
    }
    private static async Task GenerationTests()
    {
        var backend = new UninterruptibleDiscovery();
        var coordinator = new DiscoveryCoordinator(backend);
        var vm = new ShellViewModel(coordinator);
        var pending = vm.ScanAsync();
        await backend.Started.Task;
        vm.Cancel();
        Check(!pending.IsCompleted && !vm.ScanCommand.CanExecute(null), "Cancellation leaves scan blocked until uninterruptible worker returns");
        bool refused = false;
        try { await coordinator.DiscoverAsync(default); } catch (InvalidOperationException) { refused = true; }
        Check(refused && backend.Calls == 1, "Coordinator prevents accumulation of stuck workers");
        backend.Completion.SetResult([Drive("obsolete", StorageProtocol.Nvme)]);
        await pending;
        Check(vm.State == ScanState.Cancelled && vm.NvmeEmpty, "Late cancelled-generation result rejected");
        backend = new(); vm = new(new DiscoveryCoordinator(backend)); pending = vm.ScanAsync(); await backend.Started.Task;
        vm.Close(); backend.Completion.SetResult([Drive("closed", StorageProtocol.Sata)]); await pending;
        Check(vm.SataEmpty && !vm.ScanCommand.CanExecute(null), "Close rejects outstanding results and prevents further scans");
        var controlled = new ControlledDiscovery(); vm = new(new DiscoveryCoordinator(controlled));
        pending = vm.ScanAsync(); controlled.Completion.SetResult([Drive("retained", StorageProtocol.Sata)]); await pending;
        controlled.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously); pending = vm.ScanAsync();
        controlled.Completion.SetException(new IOException("Enumeration failed")); await pending;
        Check(vm.SataDevices.Count == 1 && vm.IsStale && vm.Sata.DataStatus.Contains("Stale"), "Enumeration failure retains and visibly marks previous inventory stale");
        controlled.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously); pending = vm.ScanAsync(); controlled.Completion.SetResult([]); await pending;
        Check(vm.SataEmpty && !vm.IsStale && vm.Sata.Device is null, "Empty successful enumeration clears previous device and stale status");
        controlled.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously); pending = vm.ScanAsync(); controlled.Completion.SetResult([Drive("usb", StorageProtocol.Unknown)]); await pending;
        Check(vm.OtherExpanded && vm.OperationStatus.Contains("Unidentified"), "Unknown-only discovery directs attention to Unidentified devices in Diagnostics");
        controlled.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = vm.ScanAsync();
        controlled.Completion.SetResult([Drive("nvme", StorageProtocol.Nvme), Drive("sata1", StorageProtocol.Sata), Drive("sata2", StorageProtocol.Sata)]);
        await pending;
        Check(vm.AllDevices.Count == 3 && vm.NvmeDevices.Count == 1 && vm.SataDevices.Count == 2 && vm.OperationStatus.Contains("1 NVMe + 2 SATA"), "Mixed three-disk inventory and protocol counts retained");
        vm.SelectedDisk = vm.SataDevices[1];
        Check(vm.SelectedSata?.Id == "sata2" && vm.ActiveTab == 0, "All-disk selector selects second SATA device");
        vm.SelectedDisk = vm.NvmeDevices[0];
        Check(vm.SelectedNvme?.Id == "nvme" && vm.ActiveTab == 1, "All-disk selector switches to NVMe");
        controlled.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = vm.ScanAsync();
        controlled.Completion.SetResult([Drive("blocked-generic", StorageProtocol.Unknown) with { Bus = ConnectionBus.Raid, CapacityBytes = 1_000_000_000 }]);
        await pending; vm.SelectedDisk = vm.AllDevices.Single();
        Check(vm.WorkspaceIndex == 2 && vm.OtherDevices.Count == 1 && vm.Other.Device?.Id == "blocked-generic", "Unconfirmed physical drive routes to Additional Devices after removing duplicate overview");
        var once = new CountedDiscovery(); vm = new(new DiscoveryCoordinator(once));
        await vm.StartInitialScanAsync(); await vm.StartInitialScanAsync();
        Check(once.Calls == 1, "Shell visibility triggers at most one automatic discovery");
    }
    private sealed class UninterruptibleDiscovery : IDeviceDiscovery
    {
        public bool IsAvailable => true;
        public string UnavailableReason => "";
        public int Calls;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<StorageDevice>> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<StorageDevice>> DiscoverAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref Calls); Started.SetResult(); return Completion.Task; }
    }
    private sealed class CountedDiscovery : IDeviceDiscovery
    {
        public bool IsAvailable => true;
        public string UnavailableReason => "";
        public int Calls;
        public Task<IReadOnlyList<StorageDevice>> DiscoverAsync(CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return Task.FromResult<IReadOnlyList<StorageDevice>>([]); }
    }
}
