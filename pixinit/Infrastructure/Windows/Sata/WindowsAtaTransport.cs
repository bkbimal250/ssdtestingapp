using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.Infrastructure.Windows.Interop;

namespace pixinit.Infrastructure.Windows.Sata;

internal interface IAtaSession : IDisposable { AtaCommandResult Execute(AtaOperation operation, CancellationToken token); }
internal interface IAtaTransport { IAtaSession Open(StorageDevice device, CancellationToken token); }
internal sealed class AtaTransportException(AtaOutcome outcome, string message) : Exception(message)
{ internal AtaOutcome Outcome { get; } = outcome; }

internal static class SataIdentityGuard
{
    internal static void RequireSata(StorageDevice device)
    {
        if (device.Protocol != StorageProtocol.Sata || device.Bus != ConnectionBus.Sata || device.NativeBusType != 11 || string.IsNullOrWhiteSpace(device.InterfacePath))
            throw new AtaTransportException(AtaOutcome.Unsupported, "ATA dispatch requires a confirmed direct SATA interface. NVMe, USB, RAID and unknown devices are rejected.");
    }
    internal static void Validate(StorageDevice selected, StorageDevice current)
    {
        RequireSata(selected); RequireSata(current);
        if (!string.Equals(selected.InterfacePath, current.InterfacePath, StringComparison.OrdinalIgnoreCase) ||
            (selected.DiskNumber is not null && selected.DiskNumber != current.DiskNumber) ||
            string.IsNullOrWhiteSpace(current.Model) || !string.Equals(selected.Model.Trim(), current.Model.Trim(), StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(selected.Serial) && !string.Equals(selected.Serial.Trim(), current.Serial?.Trim(), StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(selected.Firmware) && selected.Firmware != current.Firmware) ||
            (selected.CapacityBytes is not null && selected.CapacityBytes != current.CapacityBytes))
            throw new AtaTransportException(AtaOutcome.IdentityMismatch, "Current interface metadata differs from the selected discovery record or could not be revalidated. No ATA command sent; rescan drives.");
    }
}

internal sealed class WindowsAtaTransport : IAtaTransport
{
    private static long dispatchCount;
    internal static long DispatchCount => Interlocked.Read(ref dispatchCount);
    public IAtaSession Open(StorageDevice device, CancellationToken token)
    {
        SataIdentityGuard.RequireSata(device);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new AtaTransportException(AtaOutcome.Unsupported, "Buffered ATA transport is validated for Windows x64 only.");
        token.ThrowIfCancellationRequested();
        // A read-only metadata check before the read/write-access open; no ATA sent.
        var current = WindowsDiskDiscovery.ReadDevice(device.InterfacePath!, device.InstanceId, token);
        if (current.Access == DeviceAccess.AccessDenied) throw new AtaTransportException(AtaOutcome.AccessDenied, "Metadata identity revalidation denied; no ATA command sent.");
        SataIdentityGuard.Validate(device, current);
        token.ThrowIfCancellationRequested();
        var handle = StorageNative.CreateFileW(device.InterfacePath!, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            throw new AtaTransportException(AtaCommandPolicy.ErrorOutcome(error), AtaCommandPolicy.ErrorExplanation(error));
        }
        return new Session(handle, device);
    }
    private sealed class Session(SafeFileHandle handle, StorageDevice selected) : IAtaSession
    {
        public AtaCommandResult Execute(AtaOperation operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            byte[] Query(uint code, byte[]? input, int length)
            {
                token.ThrowIfCancellationRequested();
                if (length is < 0 or > DescriptorParser.MaxBuffer) throw new System.IO.InvalidDataException("Metadata allocation limit.");
                var output = new byte[length];
                if (!StorageNative.DeviceIoControl(handle, code, input, (uint)(input?.Length ?? 0), output, (uint)length, out uint returned, IntPtr.Zero)) throw StorageNative.Error("Identity revalidation query");
                if (returned > length) throw new System.IO.InvalidDataException("Identity returned length invalid.");
                return output[..(int)returned];
            }
            // Recheck metadata on the very handle used for every ATA command.
            var current = WindowsDiskDiscovery.ReadDevice(selected.InterfacePath!, selected.InstanceId, token, Query);
            if (current.Access == DeviceAccess.AccessDenied) throw new AtaTransportException(AtaOutcome.AccessDenied, "Identity revalidation denied on the open handle.");
            SataIdentityGuard.Validate(selected, current);
            var request = AtaCommandPolicy.Request(operation);
            AtaCommandPolicy.ValidateRequest(operation, request);
            var outputBuffer = new byte[request.Length];
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref dispatchCount);
            // IOCTL_ATA_PASS_THROUGH = CTL_CODE(4, 0x40b, METHOD_BUFFERED, READ|WRITE).
            // For data-in/non-data, input length is header only, output is header + payload.
            bool success = StorageNative.DeviceIoControl(handle, 0x0004D02C, request, AtaCommandPolicy.HeaderSize,
                outputBuffer, (uint)outputBuffer.Length, out uint returnedBytes, IntPtr.Zero);
            int nativeError = success ? 0 : Marshal.GetLastWin32Error();
            // Do not dispose/cancel buffers during the synchronous native call.
            return AtaCommandPolicy.ValidateResponse(operation, outputBuffer, returnedBytes, success, nativeError);
        }
        public void Dispose() => handle.Dispose();
    }
}
