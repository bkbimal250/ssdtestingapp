using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Windows.Discovery;
using pixinit.Infrastructure.Windows.Interop;

namespace pixinit.Infrastructure.Windows.Sata;

internal interface IAtaSession : IDisposable
{
    AtaCommandResult Execute(AtaOperation operation, CancellationToken token);
    AtaCommandResult ExecuteSatSmartData(CancellationToken token) => AtaCommandResult.Missing(AtaOperation.SmartData, AtaOutcome.Unsupported, "SCSI SAT fallback is not implemented by this transport.");
    AtaCommandResult ExecuteProtocolSpecificSmartData(CancellationToken token) => AtaCommandResult.Missing(AtaOperation.SmartData, AtaOutcome.Unsupported, "ATA protocol-specific SMART query is not implemented by this transport.");
}
internal interface IAtaTransport { IAtaSession Open(StorageDevice device, CancellationToken token); }
internal sealed class AtaTransportException(AtaOutcome outcome, string message) : Exception(message)
{ internal AtaOutcome Outcome { get; } = outcome; }

internal static class SataIdentityGuard
{
    internal static void RequireSata(StorageDevice device)
    {
        if (!device.CanQuerySata || string.IsNullOrWhiteSpace(device.InterfacePath))
            throw new AtaTransportException(AtaOutcome.Unsupported, "ATA dispatch requires a direct SATA interface or a SATA-classified heuristic path. NVMe, USB and unknown devices are rejected.");
    }
    internal static void Validate(StorageDevice selected, StorageDevice current)
    {
        RequireSata(selected);
        if (selected.ProtocolHeuristic)
        {
            if (current.Bus != selected.Bus || !string.Equals(selected.InterfacePath, current.InterfacePath, StringComparison.OrdinalIgnoreCase) ||
                (selected.DiskNumber is not null && selected.DiskNumber != current.DiskNumber) ||
                (selected.CapacityBytes is not null && selected.CapacityBytes != current.CapacityBytes))
                throw new AtaTransportException(AtaOutcome.IdentityMismatch, "Current RAID disk metadata differs from the selected device. No ATA command sent; rescan drives.");
            return;
        }
        RequireSata(current);
        if (selected.Bus != current.Bus || selected.NativeBusType != current.NativeBusType || !string.Equals(selected.InterfacePath, current.InterfacePath, StringComparison.OrdinalIgnoreCase) ||
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
        [StructLayout(LayoutKind.Sequential)]
        private struct ScsiPassThroughDirect
        {
            public ushort Length;
            public byte ScsiStatus, PathId, TargetId, Lun, CdbLength, SenseInfoLength, DataIn;
            public uint DataTransferLength, TimeOutValue;
            public IntPtr DataBuffer;
            public uint SenseInfoOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] Cdb;
        }
        private void Revalidate(CancellationToken token)
        {
            byte[] Query(uint code, byte[]? input, int length)
            {
                token.ThrowIfCancellationRequested();
                if (length is < 0 or > DescriptorParser.MaxBuffer) throw new System.IO.InvalidDataException("Metadata allocation limit.");
                var output = new byte[length];
                if (!StorageNative.DeviceIoControl(handle, code, input, (uint)(input?.Length ?? 0), output, (uint)length, out uint returned, IntPtr.Zero)) throw StorageNative.Error("Identity revalidation query");
                if (returned > length) throw new System.IO.InvalidDataException("Identity returned length invalid.");
                return output[..(int)returned];
            }
            var current = WindowsDiskDiscovery.ReadDevice(selected.InterfacePath!, selected.InstanceId, token, Query);
            if (current.Access == DeviceAccess.AccessDenied) throw new AtaTransportException(AtaOutcome.AccessDenied, "Identity revalidation denied on the open handle.");
            SataIdentityGuard.Validate(selected, current);
        }
        public AtaCommandResult Execute(AtaOperation operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            byte[] Query(uint code, byte[]? input, int length)
            {
                token.ThrowIfCancellationRequested();
                if (length is < 0 or > DescriptorParser.MaxBuffer) throw new System.IO.InvalidDataException("Metadata allocation limit.");
                var output = new byte[length];
                if (!StorageNative.DeviceIoControl(handle, code, input, code == 0x0004D02C ? AtaCommandPolicy.HeaderSize : (uint)(input?.Length ?? 0), output, (uint)length, out uint returned, IntPtr.Zero)) throw StorageNative.Error("Identity revalidation query");
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
        public AtaCommandResult ExecuteSatSmartData(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (selected.Bus != ConnectionBus.Raid || selected.Protocol != StorageProtocol.Sata || !selected.CanQuerySata)
                return AtaCommandResult.Missing(AtaOperation.SmartData, AtaOutcome.Unsupported, "SAT SMART fallback is restricted to a selected SATA-classified RAID device.");
            Revalidate(token);
            int size = Marshal.SizeOf<ScsiPassThroughDirect>();
            var data = new byte[512];
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            IntPtr header = IntPtr.Zero;
            try
            {
                var cdb = new byte[16];
                cdb[0] = 0x85; cdb[1] = 0x08; cdb[2] = 0x2E; // ATA PASS-THROUGH(16), PIO data-in, 512-byte block.
                cdb[4] = 0xD0; cdb[6] = 0x01; cdb[9] = 0x4F; cdb[11] = 0xC2; cdb[14] = 0xB0; // SMART READ DATA.
                var request = new ScsiPassThroughDirect { Length = (ushort)size, CdbLength = 16, DataIn = 1, DataTransferLength = 512, TimeOutValue = 10, DataBuffer = pin.AddrOfPinnedObject(), Cdb = cdb };
                header = Marshal.AllocHGlobal(size); Marshal.StructureToPtr(request, header, false);
                token.ThrowIfCancellationRequested();
                const uint ioctlScsiPassThroughDirect = 0x0004D014;
                bool success = StorageNative.DeviceIoControlPointer(handle, ioctlScsiPassThroughDirect, header, (uint)size, header, (uint)size, out uint returned, IntPtr.Zero);
                int error = success ? 0 : Marshal.GetLastWin32Error();
                if (!success) return new(AtaOperation.SmartData, AtaCommandPolicy.ErrorOutcome(error), AtaCommandPolicy.ErrorExplanation(error), [], [], DateTimeOffset.UtcNow, error);
                if (returned < size) return AtaCommandResult.Missing(AtaOperation.SmartData, AtaOutcome.InvalidResponse, "SCSI SAT response header was truncated.");
                var response = Marshal.PtrToStructure<ScsiPassThroughDirect>(header);
                if (response.ScsiStatus != 0 || response.DataTransferLength != 512)
                    return new(AtaOperation.SmartData, AtaOutcome.InvalidResponse, $"SCSI SAT SMART response incomplete; SCSI status=0x{response.ScsiStatus:X2}; bytes={response.DataTransferLength}.", [], [], DateTimeOffset.UtcNow);
                return new(AtaOperation.SmartData, AtaOutcome.Success, "SMART READ DATA returned by SCSI SAT fallback after ATA passthrough did not provide a valid buffer.", data, [], DateTimeOffset.UtcNow);
            }
            finally { if (header != IntPtr.Zero) Marshal.FreeHGlobal(header); pin.Free(); }
        }
        public AtaCommandResult ExecuteProtocolSpecificSmartData(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (selected.Protocol != StorageProtocol.Sata || !selected.CanQuerySata)
                return AtaCommandResult.Missing(AtaOperation.SmartData, AtaOutcome.Unsupported, "Protocol-specific SMART fallback is restricted to a selected SATA-classified device.");
            Revalidate(token);
            // STORAGE_PROPERTY_QUERY + STORAGE_PROTOCOL_SPECIFIC_DATA. ATA protocol log-page
            // request 0xD0 is attempted read-only; support is driver/device dependent.
            var input = new byte[48];
            BitConverter.GetBytes(50U).CopyTo(input, 0); // StorageDeviceProtocolSpecificProperty.
            BitConverter.GetBytes(0U).CopyTo(input, 4);  // PropertyStandardQuery.
            BitConverter.GetBytes(2U).CopyTo(input, 8);  // ProtocolTypeAta.
            BitConverter.GetBytes(2U).CopyTo(input, 12); // AtaDataTypeLogPage.
            BitConverter.GetBytes(0xD0U).CopyTo(input, 16); // SMART READ DATA request value.
            BitConverter.GetBytes(40U).CopyTo(input, 24); // Offset from protocol-specific structure.
            BitConverter.GetBytes(512U).CopyTo(input, 28);
            var output = new byte[560];
            token.ThrowIfCancellationRequested();
            bool success = StorageNative.DeviceIoControl(handle, StorageNative.QueryProperty, input, (uint)input.Length, output, (uint)output.Length, out uint returned, IntPtr.Zero);
            int error = success ? 0 : Marshal.GetLastWin32Error();
            if (!success) return new(AtaOperation.SmartData, AtaCommandPolicy.ErrorOutcome(error), AtaCommandPolicy.ErrorExplanation(error), [], [], DateTimeOffset.UtcNow, error);
            if (returned < output.Length || BitConverter.ToUInt32(output, 0) < 48 || BitConverter.ToUInt32(output, 8) != 2 ||
                BitConverter.ToUInt32(output, 12) != 2 || BitConverter.ToUInt32(output, 24) != 40 || BitConverter.ToUInt32(output, 28) != 512)
                return AtaCommandResult.Missing(AtaOperation.SmartData, AtaOutcome.InvalidResponse, "ATA protocol-specific descriptor was truncated or did not match the requested SMART data layout.");
            var payload = output.AsSpan(48, 512).ToArray();
            return new(AtaOperation.SmartData, AtaOutcome.Success, "ATA protocol-specific log-page query returned 512 bytes; SMART attribute layout is validated separately.", payload, [], DateTimeOffset.UtcNow);
        }
        public void Dispose() => handle.Dispose();
    }
}
