using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.Infrastructure.Windows.Interop;

namespace pixinit.Infrastructure.Windows.Nvme;

internal interface INvmeTransport { NvmeResponse Query(StorageDevice device, NvmeOperation operation, CancellationToken token, uint namespaceId = 0); }
internal static class NvmeQueryPolicy
{
    internal static int Length(NvmeOperation op) => op switch { NvmeOperation.Controller or NvmeOperation.Namespace => 4096, NvmeOperation.Health or NvmeOperation.Errors => 512, _ => throw new ArgumentOutOfRangeException(nameof(op)) };
    internal static byte[] Request(NvmeOperation op, uint ns = 0)
    {
        if ((op == NvmeOperation.Namespace && ns is 0 or uint.MaxValue) || (op != NvmeOperation.Namespace && ns != 0)) throw new InvalidDataException("Invalid query namespace scope.");
        var b = new byte[48 + Length(op)];
        void W(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
        W(0, op is NvmeOperation.Namespace or NvmeOperation.Health ? 50U : 49U); // device vs adapter protocol property
        W(8, 3); W(12, op is NvmeOperation.Controller or NvmeOperation.Namespace ? 1U : 2U);
        W(16, op switch { NvmeOperation.Controller => 1, NvmeOperation.Namespace => 0, NvmeOperation.Health => 2, _ => 1 });
        W(20, ns); W(24, 40); W(28, (uint)Length(op));
        return b;
    }
    internal static void ValidateRequest(NvmeOperation op, byte[] b, uint ns = 0)
    { if (!Request(op, ns).AsSpan().SequenceEqual(b)) throw new InvalidDataException("NVMe read-query allowlist violation."); }
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
    { if (d.Protocol != StorageProtocol.Nvme || d.Bus != ConnectionBus.Nvme || d.NativeBusType != 17 || string.IsNullOrWhiteSpace(d.InterfacePath)) throw new InvalidOperationException("Only a confirmed direct NVMe interface can receive NVMe queries."); }
    internal static bool Matches(StorageDevice a, StorageDevice b) => b.Protocol == StorageProtocol.Nvme && b.Bus == ConnectionBus.Nvme && b.NativeBusType == 17 &&
        string.Equals(a.InterfacePath, b.InterfacePath, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(b.Model) && a.Model.Trim() == b.Model.Trim() &&
        (a.DiskNumber is null || a.DiskNumber == b.DiskNumber) && (string.IsNullOrWhiteSpace(a.Serial) || a.Serial.Trim() == b.Serial?.Trim()) &&
        (a.Firmware is null || a.Firmware == b.Firmware) && (a.CapacityBytes is null || a.CapacityBytes == b.CapacityBytes);
}
internal sealed class WindowsNvmeTransport : INvmeTransport
{
    private static long dispatchCount;
    internal static long DispatchCount => Interlocked.Read(ref dispatchCount);
    public NvmeResponse Query(StorageDevice device, NvmeOperation op, CancellationToken token, uint namespaceId = 0)
    {
        NvmeIdentityGuard.Require(device);
        var request = NvmeQueryPolicy.Request(op, namespaceId); NvmeQueryPolicy.ValidateRequest(op, request, namespaceId);
        NvmeResponse Fail(NvmeOutcome status, string reason) => new(op, status, reason, [], DateTimeOffset.UtcNow, NvmeQueryPolicy.Scope(op, namespaceId));
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return Fail(NvmeOutcome.Unsupported, "Validated transport requires Windows x64.");
        token.ThrowIfCancellationRequested();
        var current = WindowsDiskDiscovery.ReadDevice(device.InterfacePath!, device.InstanceId, token);
        if (current.Access == DeviceAccess.AccessDenied) return Fail(NvmeOutcome.AccessDenied, "Identity revalidation denied.");
        if (!NvmeIdentityGuard.Matches(device, current)) return Fail(NvmeOutcome.IdentityMismatch, "Identity changed or unavailable; rescan. No protocol query dispatched.");
        using SafeFileHandle handle = StorageNative.CreateFileW(device.InterfacePath!, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
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
        token.ThrowIfCancellationRequested();
        var result = new byte[request.Length];
        Interlocked.Increment(ref dispatchCount);
        bool ok = StorageNative.DeviceIoControl(handle, StorageNative.QueryProperty, request, (uint)request.Length, result, (uint)result.Length, out uint returned, IntPtr.Zero);
        int error = ok ? 0 : Marshal.GetLastWin32Error();
        return NvmeQueryPolicy.Validate(op, result, returned, ok, error, namespaceId);
    }
}
