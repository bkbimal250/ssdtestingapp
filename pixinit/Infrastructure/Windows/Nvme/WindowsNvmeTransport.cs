using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Infrastructure.Logging;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.Infrastructure.Windows.Interop;

namespace pixinit.Infrastructure.Windows.Nvme;

internal interface INvmeTransport { NvmeResponse Query(StorageDevice device, NvmeOperation operation, CancellationToken token, uint namespaceId = 0); }
internal static class NvmeQueryPolicy
{
    internal static int Length(NvmeOperation op) => op switch { NvmeOperation.Controller or NvmeOperation.Namespace => 4096, NvmeOperation.Health or NvmeOperation.Errors => 512, _ => throw new ArgumentOutOfRangeException(nameof(op)) };
    internal static byte[] Request(NvmeOperation op, uint ns = 0, bool raid = false)
    {
        if ((op == NvmeOperation.Namespace && ns is 0 or uint.MaxValue) || (op != NvmeOperation.Namespace && ns != 0)) throw new InvalidDataException("Invalid query namespace scope.");
        return Request(op, ns, raid, false);
    }
    internal static byte[] Request(NvmeOperation op, uint ns, bool raid, bool alternateProperty)
    {
        if ((op == NvmeOperation.Namespace && ns is 0 or uint.MaxValue) || (op != NvmeOperation.Namespace && ns != 0)) throw new InvalidDataException("Invalid query namespace scope.");
        var b = new byte[48 + Length(op)];
        void W(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
        uint property = raid || op is NvmeOperation.Namespace or NvmeOperation.Health ? 50U : 49U;
        if (alternateProperty) property = property == 50U ? 49U : 50U;
        W(0, property); // 49 adapter property, 50 device property. Both are read-only protocol queries.
        W(8, 3); W(12, op is NvmeOperation.Controller or NvmeOperation.Namespace ? 1U : 2U);
        W(16, op switch { NvmeOperation.Controller => 1, NvmeOperation.Namespace => 0, NvmeOperation.Health => 2, _ => 1 });
        W(20, ns); W(24, 40); W(28, (uint)Length(op));
        return b;
    }
    internal static void ValidateRequest(NvmeOperation op, byte[] b, uint ns = 0, bool raid = false)
    {
        if (!Request(op, ns, raid).AsSpan().SequenceEqual(b) && !Request(op, ns, raid, true).AsSpan().SequenceEqual(b))
            throw new InvalidDataException("NVMe read-query allowlist violation.");
    }
    internal static NvmeOutcome Error(int e) => e switch { 5 => NvmeOutcome.AccessDenied, 1 or 50 => NvmeOutcome.Unsupported, 2 or 21 or 1167 => NvmeOutcome.Disconnected, _ => NvmeOutcome.Failed };
    internal static string Scope(NvmeOperation op, uint ns = 0) => op switch
    {
        NvmeOperation.Namespace => $"Namespace {ns} (established mapping)",
        NvmeOperation.Health => "Selected physical NVMe device through Windows; controller versus namespace scope is unresolved until a numeric NSID is established",
        _ => "Controller-wide (adapter query); not exclusive to the selected namespace"
    };
    internal static NvmeResponse Validate(NvmeOperation op, byte[] b, uint returned, bool success, int error, uint ns = 0)
    {
        var now = DateTimeOffset.UtcNow;
        byte[] raw = returned <= b.Length ? b.AsSpan(0, (int)returned).ToArray() : [];
        NvmeResponse Fail(NvmeOutcome outcome, string reason) => new(op, outcome, reason, [], now, Scope(op, ns), NativeError: success ? null : error) { RawResponse = raw };
        if (!success) return Fail(Error(error), $"Windows error {error}; {(error == 5 ? "access denied; no automatic elevation" : error is 1 or 50 ? "request unsupported by Windows stack" : "cause not otherwise established")}");
        if (returned > b.Length || returned < 48) return Fail(NvmeOutcome.InvalidResponse, "Truncated or oversized descriptor.");
        uint R(int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
        uint offset = R(24), length = R(28);
        if (R(0) != 48 || R(4) != 48 || R(8) != 3 || R(12) != (op is NvmeOperation.Controller or NvmeOperation.Namespace ? 1U : 2U) ||
            offset < 40 || length != Length(op) || (ulong)8 + offset + length > returned || b.Length > 4144)
            return Fail(NvmeOutcome.InvalidResponse, "Invalid descriptor version/size/type/offset/length; offset is relative to ProtocolSpecificData.");
        return new(op, NvmeOutcome.Success, "Validated Windows protocol descriptor. Fixed return DWORD is retained; this query path provides no full NVMe completion status.", b.AsSpan(checked(8 + (int)offset), (int)length).ToArray(), now, Scope(op, ns), R(32)) { RawResponse = raw };
    }
}
internal static class NvmeIdentityGuard
{
    internal static void Require(StorageDevice d)
    { if (!d.CanQueryNvme || string.IsNullOrWhiteSpace(d.InterfacePath)) throw new InvalidOperationException("An NVMe query route and device interface are required."); }
    internal static bool Matches(StorageDevice a, StorageDevice b)
    {
        if (a.Bus != b.Bus || a.NativeBusType != b.NativeBusType || !string.Equals(a.InterfacePath, b.InterfacePath, StringComparison.OrdinalIgnoreCase) ||
            (a.DiskNumber is not null && a.DiskNumber != b.DiskNumber) || (a.CapacityBytes is not null && a.CapacityBytes != b.CapacityBytes)) return false;
        if (a.UsbNvmeAdmin)
            return true;
        if (a.ProtocolHeuristic) return true;
        return b.CanQueryNvme && !string.IsNullOrWhiteSpace(b.Model) && a.Model.Trim() == b.Model.Trim() &&
            (string.IsNullOrWhiteSpace(a.Serial) || a.Serial.Trim() == b.Serial?.Trim()) && (a.Firmware is null || a.Firmware == b.Firmware);
    }
}
internal sealed class WindowsNvmeTransport : INvmeTransport
{
    private static long dispatchCount;
    internal static long DispatchCount => Interlocked.Read(ref dispatchCount);
    private static SafeFileHandle Open(StorageDevice device)
    {
        if (!device.UsbNvmeAdmin) return StorageNative.CreateFileW(device.InterfacePath!, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        SafeFileHandle? last = null;
        foreach (uint access in new uint[] { 0xC0000000, 0x80000000, 0 })
        {
            var handle = StorageNative.CreateFileW(device.InterfacePath!, access, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (!handle.IsInvalid) { last?.Dispose(); return handle; }
            last?.Dispose();
            last = handle;
        }
        return last ?? StorageNative.CreateFileW(device.InterfacePath!, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    }
    public NvmeResponse Query(StorageDevice device, NvmeOperation op, CancellationToken token, uint namespaceId = 0)
    {
        NvmeIdentityGuard.Require(device);
        var request = NvmeQueryPolicy.Request(op, namespaceId, device.RaidProtocolVerified); NvmeQueryPolicy.ValidateRequest(op, request, namespaceId, device.RaidProtocolVerified);
        NvmeResponse Fail(NvmeOutcome status, string reason) => new(op, status, reason, [], DateTimeOffset.UtcNow, NvmeQueryPolicy.Scope(op, namespaceId));
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return Fail(NvmeOutcome.Unsupported, "Validated transport requires Windows x64.");
        token.ThrowIfCancellationRequested();
        var current = WindowsDiskDiscovery.ReadDevice(device.InterfacePath!, device.InstanceId, token);
        if (current.Access == DeviceAccess.AccessDenied) return Fail(NvmeOutcome.AccessDenied, "Identity revalidation denied.");
        if (!NvmeIdentityGuard.Matches(device, current)) return Fail(NvmeOutcome.IdentityMismatch, "Identity changed or unavailable; rescan. No protocol query dispatched.");
        using SafeFileHandle handle = Open(device);
        if (handle.IsInvalid) { int e = Marshal.GetLastWin32Error(); return Fail(NvmeQueryPolicy.Error(e), $"Open failed, Windows error {e}; no automatic elevation."); }
        byte[] QueryMetadata(uint code, byte[]? input, int length)
        {
            token.ThrowIfCancellationRequested();
            if (length is < 0 or > DescriptorParser.MaxBuffer) throw new InvalidDataException("Metadata allocation bound.");
            var output = new byte[length];
            if (!StorageNative.DeviceIoControl(handle, code, input, (uint)(input?.Length ?? 0), output, (uint)length, out uint n, IntPtr.Zero)) throw StorageNative.Error("NVMe identity revalidation");
            if (n > length) throw new InvalidDataException("Metadata length invalid.");
            return output[..(int)n];
        }
        current = WindowsDiskDiscovery.ReadDevice(device.InterfacePath!, device.InstanceId, token, QueryMetadata);
        if (!NvmeIdentityGuard.Matches(device, current)) return Fail(NvmeOutcome.IdentityMismatch, "Same-handle identity revalidation failed; no query dispatched.");
        if (device.UsbNvmeAdmin)
        {
            if (op is NvmeOperation.Errors or NvmeOperation.Namespace)
                return Fail(NvmeOutcome.Unsupported, "USB NVMe admin pass-through reads Identify Controller and SMART/Health. SAT was not sent.");
            byte[]? payload = op == NvmeOperation.Controller ? JmicronNvmeAdmin.ReadIdentify(handle) : op == NvmeOperation.Health ? JmicronNvmeAdmin.ReadHealth(handle) : null;
            string source = "NVMe Admin pass-through via SCSI/UASP";
            if (payload is null && JmicronNvmeAdmin.LastError == 5) return new NvmeResponse(op, NvmeOutcome.AccessDenied, "NVMe admin pass-through needs administrator. SAT was not sent.", [], DateTimeOffset.UtcNow, NvmeQueryPolicy.Scope(op), NativeError: 5) { Source = source };
            if (payload is null) return new NvmeResponse(op, NvmeOutcome.Failed, "NVMe admin pass-through via SCSI/UASP returned no payload. SAT was not sent.", [], DateTimeOffset.UtcNow, NvmeQueryPolicy.Scope(op)) { Source = source };
            return new NvmeResponse(op, NvmeOutcome.Success, "NVMe admin pass-through via SCSI/UASP.", payload, DateTimeOffset.UtcNow,
                op == NvmeOperation.Controller ? "Controller-wide UASP admin identify" : "SMART/Health log 02h via UASP admin pass-through") { Source = source };
        }
        NvmeResponse Send(byte[] query, bool headerOnly)
        {
            NvmeQueryPolicy.ValidateRequest(op, query, namespaceId, device.RaidProtocolVerified);
            token.ThrowIfCancellationRequested();
            var result = new byte[query.Length];
            uint input = headerOnly ? 48u : (uint)query.Length;
            Interlocked.Increment(ref dispatchCount);
            bool ok = StorageNative.DeviceIoControl(handle, StorageNative.QueryProperty, query, input, result, (uint)result.Length, out uint returned, IntPtr.Zero);
            int error = ok ? 0 : Marshal.GetLastWin32Error();
            var response = NvmeQueryPolicy.Validate(op, result, returned, ok, error, namespaceId);
            string property = query[0] == 50 ? "device property 50" : "adapter property 49";
            return response with { Explanation = $"{response.Explanation} ({property}, input {(headerOnly ? "header" : "full")})" };
        }
        var attempts = new List<NvmeResponse> { Send(request, false) };
        if (!attempts[0].Succeeded && attempts[0].Outcome is not (NvmeOutcome.AccessDenied or NvmeOutcome.IdentityMismatch))
        {
            attempts.Add(Send(request, true));
            if (!attempts[^1].Succeeded)
            {
                var alternate = NvmeQueryPolicy.Request(op, namespaceId, device.RaidProtocolVerified, true);
                attempts.Add(Send(alternate, false));
                if (!attempts[^1].Succeeded) attempts.Add(Send(alternate, true));
            }
        }
        var response = attempts.FirstOrDefault(item => item.Succeeded) ?? attempts[0];
        if (!response.Succeeded && attempts.Count > 1)
            response = response with { Explanation = string.Join(" | ", attempts.Select(item => item.Explanation)) };
        return device.RaidProtocolVerified ? response with { Scope = "Selected RAID-presented disk through device protocol property; RAID driver controller/namespace scope is not independently established" } : response;
    }
}

internal static class NvmeControllerIdentity
{
    internal static StorageDevice Apply(StorageDevice device, Func<uint, byte[]?, int, byte[]> query)
    {
        if (device.Bus != ConnectionBus.Nvme || device.Protocol != StorageProtocol.Nvme) return device;
        try
        {
            var request = NvmeQueryPolicy.Request(NvmeOperation.Controller);
            var raw = query(StorageNative.QueryProperty, request, request.Length);
            var response = NvmeQueryPolicy.Validate(NvmeOperation.Controller, raw, (uint)raw.Length, true, 0);
            if (!response.Succeeded || response.Payload.Length != 4096) return device;
            var controller = NvmeControllerParser.Parse(response.Payload);
            if (string.IsNullOrWhiteSpace(controller.Serial)) return device;
            DiscoveryLog.Write($"NVMe Identify serial preferred: disk={device.DiskNumber}; ending={controller.Serial.Trim()[^Math.Min(4, controller.Serial.Trim().Length)..]}.");
            return device with
            {
                Serial = controller.Serial.Trim(),
                Firmware = string.IsNullOrWhiteSpace(controller.Firmware) ? device.Firmware : controller.Firmware.Trim()
            };
        }
        catch (Exception ex)
        {
            DiscoveryLog.Write("NVMe Identify serial was not applied", ex);
            return device;
        }
    }
}
