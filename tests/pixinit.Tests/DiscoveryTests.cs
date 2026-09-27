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
        byte[] BadCapacity(uint code, byte[]? input, int size) => code == StorageNative.GeometryEx ? [1, 2] : Query(code, input, size);
        var incomplete = WindowsDiskDiscovery.ReadDevice("incomplete", null, default, BadCapacity);
        Check(incomplete.Protocol == StorageProtocol.Sata && incomplete.CapacityBytes is null && incomplete.Model == first.Model && incomplete.Limitations.Contains("capacity"), "Invalid capacity preserves a classified device with other real identity fields");
        Check(first.CapacityBytes == 500_000_000_000 && first.DiskNumber == 2 && first.Firmware is null, "Real query fields retained when optional firmware/media unavailable");
        Check(WindowsDiskDiscovery.Deduplicate([first, second, first]).Count == 2, "Duplicate path records removed; matching serials do not merge distinct disks");
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
        Check(vm.OtherExpanded && vm.OperationStatus.Contains("Other"), "Unknown-only discovery directs attention to Other / Unidentified");
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
