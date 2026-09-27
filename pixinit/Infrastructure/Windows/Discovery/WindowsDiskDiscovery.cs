using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using pixinit.Core.Abstractions;
using pixinit.Core.Devices;
using pixinit.Infrastructure.Logging;
using pixinit.Infrastructure.Windows.Interop;

namespace pixinit.Infrastructure.Windows.Discovery;

// All native calls execute on the coordinator's single background worker. They are
// synchronous: cancellation is checked between calls, not advertised as native abort.
public sealed class WindowsDiskDiscovery : IDeviceDiscovery
{
    public bool IsAvailable => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;
    public string UnavailableReason => "Windows x64 is the validated discovery architecture.";
    public Task<IReadOnlyList<StorageDevice>> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable) throw new PlatformNotSupportedException(UnavailableReason);
        try { return Task.FromResult(Discover(cancellationToken)); }
        catch (OperationCanceledException) { DiscoveryLog.Write("Discovery cancelled; results discarded."); throw; }
        catch (Exception ex) { DiscoveryLog.Write("Discovery failed; inventory must be retained", ex); throw; }
    }

    private static IReadOnlyList<StorageDevice> Discover(CancellationToken token)
    {
        DiscoveryLog.Write("Disk discovery started (SetupAPI, read-only handles).");
        var devices = new List<StorageDevice>();
        using var set = StorageNative.SetupDiGetClassDevsW(StorageNative.DiskInterface, null, IntPtr.Zero, 0x12);
        if (set.IsInvalid) throw StorageNative.Error("Disk interface enumeration could not start");
        for (uint index = 0; ; index++)
        {
            token.ThrowIfCancellationRequested();
            var data = new StorageNative.InterfaceData { Size = (uint)Marshal.SizeOf<StorageNative.InterfaceData>() };
            if (!StorageNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, StorageNative.DiskInterface, index, ref data))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 259) break;
                throw new Win32Exception(error, "Disk enumeration incomplete; previous inventory must be retained");
            }
            var info = new StorageNative.DeviceInfo { Size = (uint)Marshal.SizeOf<StorageNative.DeviceInfo>() };
            StorageNative.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint required, ref info);
            int sizingError = Marshal.GetLastWin32Error();
            if (sizingError != 122 || required < 8 || required > DescriptorParser.MaxBuffer)
                throw new InvalidDataException($"Disk interface detail sizing failed ({sizingError}); enumeration incomplete.");
            var memory = Marshal.AllocHGlobal(checked((int)required));
            string path;
            try
            {
                Marshal.WriteInt32(memory, 8); // Unicode SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize on x64.
                if (!StorageNative.SetupDiGetDeviceInterfaceDetailW(set, ref data, memory, required, out uint actual, ref info))
                    throw StorageNative.Error("Disk interface path unavailable; enumeration incomplete");
                if (actual < 8 || actual > required) throw new InvalidDataException("Invalid interface detail size.");
                var bytes = new byte[actual - 4]; Marshal.Copy(IntPtr.Add(memory, 4), bytes, 0, bytes.Length);
                path = ReadWidePath(bytes);
            }
            finally { Marshal.FreeHGlobal(memory); }
            var instance = new StringBuilder(4096);
            string? instanceId = StorageNative.SetupDiGetDeviceInstanceIdW(set, ref info, instance, (uint)instance.Capacity, out _) ? instance.ToString() : null;
            devices.Add(ReadDevice(path, instanceId, token));
        }
        var unique = Deduplicate(devices);
        var mapped = MapVolumes(unique, token);
        token.ThrowIfCancellationRequested();
        DiscoveryLog.Write($"Discovery completed: {mapped.Count} disks; SATA={mapped.Count(d => d.Protocol == StorageProtocol.Sata)}, NVMe={mapped.Count(d => d.Protocol == StorageProtocol.Nvme)}, Other={mapped.Count(d => d.Protocol == StorageProtocol.Unknown)}.");
        return mapped;
    }

    internal static string ReadWidePath(byte[] bytes)
    {
        if (bytes.Length % 2 != 0) throw new InvalidDataException("Invalid UTF-16 path buffer.");
        for (int i = 0; i < bytes.Length - 1; i += 2)
            if (bytes[i] == 0 && bytes[i + 1] == 0)
            {
                if (i == 0) throw new InvalidDataException("Empty interface path.");
                return Encoding.Unicode.GetString(bytes, 0, i);
            }
        throw new InvalidDataException("Unterminated interface path.");
    }

    internal static StorageDevice ReadDevice(string path, string? instance, CancellationToken token,
        Func<uint, byte[]?, int, byte[]>? queryOverride = null)
    {
        var issues = new List<string>();
        // Path + PnP instance is session identity evidence. Never merge on serial/model/disk number.
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((instance ?? "") .ToUpperInvariant() + "|" + path.ToUpperInvariant())));
        var device = new StorageDevice(id, "", null, StorageProtocol.Unknown, ConnectionBus.Unknown, DiagnosticCapabilities.None, DeviceAccess.Limited)
        {
            InterfacePath = path, InstanceId = instance, Source = "SetupAPI + Windows storage property queries",
            IdentityEvidence = instance is null ? "Interface path hash; session only; no cross-reconnect identity guarantee" : "PnP instance + interface path hash; session only; not a persistent hardware identity",
            ObservedAt = DateTimeOffset.UtcNow
        };
        using var handle = queryOverride is null ? StorageNative.CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero) : null;
        if (handle?.IsInvalid == true)
        {
            var ex = StorageNative.Error("Open disk metadata handle"); DiscoveryLog.Write($"Device {id[..12]} open failed", ex);
            return device with { Access = ex.NativeErrorCode == 5 ? DeviceAccess.AccessDenied : DeviceAccess.Limited,
                Limitations = $"{Friendly(ex)} Underlying protocol not established. Diagnostics not yet queried." };
        }
        byte[] Query(uint code, byte[]? input, int size)
        {
            token.ThrowIfCancellationRequested();
            return queryOverride is null ? Ioctl(handle!, code, input, size) : queryOverride(code, input, size);
        }
        bool denied = false;
        void Attempt(string label, Action query)
        {
            token.ThrowIfCancellationRequested();
            try { query(); }
            catch (Exception ex) when (ex is Win32Exception or InvalidDataException or OverflowException)
            {
                denied |= ex is Win32Exception { NativeErrorCode: 5 };
                issues.Add($"{label}: {Friendly(ex)}"); DiscoveryLog.Write($"Device {id[..12]}: {label}", ex);
            }
        }
        Attempt("Device descriptor", () =>
        {
            var descriptor = DescriptorParser.ParseDevice(QueryProperty(Query, 0));
            var classification = DescriptorParser.Classify(descriptor.Bus);
            device = device with { Model = descriptor.Model ?? "", Serial = descriptor.Serial, Firmware = descriptor.Firmware,
                NativeBusType = descriptor.Bus, Bus = classification.Bus, Protocol = classification.Protocol,
                Removable = descriptor.Removable, Capabilities = DiagnosticCapabilities.Identity };
            if (descriptor.Warnings.Length > 0) issues.Add(descriptor.Warnings);
        });
        Attempt("Disk number", () =>
        {
            var bytes = Query(StorageNative.DeviceNumber, null, 12);
            if (bytes.Length < 12 || DescriptorParser.U32(bytes, 0) != 7) throw new InvalidDataException("Invalid disk device-number response.");
            device = device with { DiskNumber = checked((int)DescriptorParser.U32(bytes, 4)) };
        });
        Attempt("Physical-disk capacity", () =>
        {
            // DISK_GEOMETRY_EX.DiskSize is at byte 24. FILE_ANY_ACCESS avoids elevation for length queries.
            var bytes = Query(StorageNative.GeometryEx, null, 256);
            if (bytes.Length < 32) throw new InvalidDataException("Truncated disk geometry.");
            long length = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(24));
            if (length < 0) throw new InvalidDataException("Negative physical capacity.");
            device = device with { CapacityBytes = length };
        });
        // Seek penalty is exposed as evidence, never treated as proof of SSD/HDD.
        Attempt("Media evidence", () =>
        {
            var bytes = QueryProperty(Query, 7);
            int size = DescriptorParser.DescriptorSize(bytes, 9);
            if (size > bytes.Length || bytes[8] > 1) throw new InvalidDataException("Invalid seek-penalty descriptor.");
            issues.Add(bytes[8] == 1 ? "Windows reports seek penalty; media type remains unconfirmed." : "Windows reports no seek penalty; this alone does not establish SSD media.");
        });
        if (device.Protocol == StorageProtocol.Unknown) issues.Add("Underlying protocol not established from reported bus; no pass-through probing performed.");
        issues.Add("Media type unknown; diagnostics not yet queried.");
        return device with { Access = denied ? DeviceAccess.AccessDenied : issues.Count > 2 ? DeviceAccess.Limited : DeviceAccess.Available, Limitations = string.Join(" ", issues) };
    }

    private static byte[] QueryProperty(Func<uint, byte[]?, int, byte[]> query, uint property)
    {
        var input = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(input, property);
        var header = query(StorageNative.QueryProperty, input, 8);
        int size = DescriptorParser.DescriptorSize(header, 8);
        return query(StorageNative.QueryProperty, input, size);
    }
    private static byte[] Ioctl(SafeFileHandle handle, uint code, byte[]? input, int size)
    {
        if (size < 0 || size > DescriptorParser.MaxBuffer) throw new InvalidDataException("Query buffer limit exceeded.");
        var buffer = new byte[size];
        if (!StorageNative.DeviceIoControl(handle, code, input, (uint)(input?.Length ?? 0), buffer, (uint)size, out uint returned, IntPtr.Zero))
            throw StorageNative.Error($"Read-only query 0x{code:X8}");
        if (returned > size) throw new InvalidDataException("Invalid returned byte count.");
        return buffer[..checked((int)returned)];
    }

    internal static IReadOnlyList<StorageDevice> Deduplicate(IEnumerable<StorageDevice> devices) =>
        devices.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var first = g.First();
            if (g.Select(d => d.Bus).Distinct().Count() > 1)
                return first with { Protocol = StorageProtocol.Unknown, Limitations = first.Limitations + " Conflicting bus evidence for the same interface; classification withheld." };
            return first;
        }).ToArray();

    internal static IReadOnlyList<StorageDevice> ApplySystemMapping(IReadOnlyList<StorageDevice> devices, uint[]? disks, string? limitation)
    {
        var candidates = disks?.Distinct().ToArray() ?? [];
        var matches = candidates.Length == 1 ? devices.Where(d => d.DiskNumber == candidates[0]).ToArray() : [];
        bool unambiguous = matches.Length == 1 && matches[0].Protocol != StorageProtocol.Unknown;
        var note = unambiguous ? "" : limitation ?? "Windows volume mapping is ambiguous or virtual; deterministic selection used.";
        return devices.Select(d => d with { IsUnambiguousSystemDisk = unambiguous && d.Id == matches[0].Id,
            Limitations = string.Join(" ", new[] { d.Limitations, note }.Where(s => s.Length > 0)) }).ToArray();
    }

    private static IReadOnlyList<StorageDevice> MapVolumes(IReadOnlyList<StorageDevice> devices, CancellationToken token)
    {
        var mounts = new Dictionary<uint, List<string>>();
        uint[]? systemDisks = null;
        string? systemVolume = null, mappingIssue = null;
        try
        {
            var windows = new StringBuilder(32768); uint length = StorageNative.GetWindowsDirectoryW(windows, (uint)windows.Capacity);
            if (length == 0 || length >= windows.Capacity) throw StorageNative.Error("Windows directory unavailable");
            var root = new StringBuilder(32768); var volume = new StringBuilder(1024);
            if (!StorageNative.GetVolumePathNameW(windows.ToString(), root, (uint)root.Capacity) ||
                !StorageNative.GetVolumeNameForVolumeMountPointW(root.ToString(), volume, (uint)volume.Capacity)) throw StorageNative.Error("Windows volume name unavailable");
            systemVolume = volume.ToString();
            systemDisks = ReadExtents(systemVolume);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidDataException)
        { mappingIssue = $"Windows volume mapping unavailable: {Friendly(ex)}"; DiscoveryLog.Write("Windows volume mapping", ex); }
        try
        {
            var name = new StringBuilder(1024);
            using var find = StorageNative.FindFirstVolumeW(name, (uint)name.Capacity);
            if (find.IsInvalid) throw StorageNative.Error("Volume enumeration unavailable");
            do
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    string volume = name.ToString();
                    var diskNumbers = volume.Equals(systemVolume, StringComparison.OrdinalIgnoreCase) && systemDisks is not null ? systemDisks : ReadExtents(volume);
                    var paths = new char[32768];
                    if (!StorageNative.GetVolumePathNamesForVolumeNameW(volume, paths, (uint)paths.Length, out uint actual)) throw StorageNative.Error("Volume mount points unavailable");
                    if (actual == 0 || actual > paths.Length || paths[actual - 1] != '\0' || (actual > 1 && paths[actual - 2] != '\0'))
                        throw new InvalidDataException("Invalid or unterminated mount-point list.");
                    var mappedPaths = new string(paths, 0, checked((int)actual)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                    foreach (uint disk in diskNumbers)
                    {
                        if (!mounts.TryGetValue(disk, out var list)) mounts[disk] = list = [];
                        list.AddRange(mappedPaths.Length == 0 ? ["Unmounted volume"] : mappedPaths);
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidDataException)
                { DiscoveryLog.Write("Volume mapping skipped", ex); mappingIssue ??= "Some volume mappings unavailable; mount list may be incomplete."; }
                if (StorageNative.FindNextVolumeW(find, name, (uint)name.Capacity)) continue;
                int error = Marshal.GetLastWin32Error();
                if (error != 18) throw new Win32Exception(error, "Volume enumeration incomplete");
                break;
            } while (true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidDataException)
        { DiscoveryLog.Write("Volume enumeration", ex); mappingIssue ??= "Volume enumeration unavailable; disk records retained."; }
        var mapped = devices.Select(d => d with { MountPoints = d.DiskNumber is int n && mounts.TryGetValue((uint)n, out var paths) ? paths.Distinct().ToArray() : [],
            Limitations = d.Limitations + (mappingIssue is null ? "" : " " + mappingIssue) }).ToArray();
        return ApplySystemMapping(mapped, systemDisks, mappingIssue);
    }
    private static uint[] ReadExtents(string volume)
    {
        using var handle = StorageNative.CreateFileW(volume.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw StorageNative.Error("Open volume metadata handle");
        for (int size = 256; size <= DescriptorParser.MaxBuffer; size *= 2)
        {
            try { return DescriptorParser.ParseExtents(Ioctl(handle, StorageNative.VolumeExtents, null, size)); }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 234 or 122) { }
        }
        throw new InvalidDataException("Volume extents exceed allocation limit.");
    }
    private static string Friendly(Exception ex) => ex is Win32Exception win32 ? win32.NativeErrorCode switch
    {
        5 => "Access denied.", 1 or 50 => "Query unsupported by the Windows device stack.",
        21 or 1167 or 2 => "Device unavailable or removed.", _ => $"Windows query failed (error {win32.NativeErrorCode})."
    } : "Invalid or incomplete Windows response.";
}
