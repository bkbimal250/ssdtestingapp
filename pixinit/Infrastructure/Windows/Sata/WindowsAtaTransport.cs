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
            throw new AtaTransportException(AtaOutcome.Unsupported, "Diagnostics require a SATA-classified direct, RAID or USB SAT path. NVMe and unknown devices are rejected.");
    }
    internal static void Validate(StorageDevice selected, StorageDevice current)
    {
        RequireSata(selected);
        if (selected.Bus == ConnectionBus.Usb)
        {
            bool stable = current.Bus == ConnectionBus.Usb && current.NativeBusType == 7 &&
                string.Equals(selected.InterfacePath, current.InterfacePath, StringComparison.OrdinalIgnoreCase) &&
                selected.DiskNumber == current.DiskNumber && selected.CapacityBytes == current.CapacityBytes;
            bool drifted = !string.Equals(selected.Model.Trim(), current.Model.Trim(), StringComparison.Ordinal) ||
                !string.Equals(selected.Serial?.Trim(), current.Serial?.Trim(), StringComparison.Ordinal) ||
                selected.Firmware != current.Firmware;
            bool bridgeIdentity = !string.IsNullOrWhiteSpace(selected.BridgeChip) || UsbBridgeProbe.IsBridgeSerial(selected.Serial) || UsbBridgeProbe.IsBridgeSerial(current.Serial);
            if (!stable || (drifted && !bridgeIdentity))
                throw new AtaTransportException(AtaOutcome.IdentityMismatch, "USB device metadata changed; no SAT command sent. Reconnect and reopen PIXINIT.");
            return;
        }
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
            if (selected.Bus == ConnectionBus.Usb)
            {
                AtaCommandResult? best = null;
                foreach (bool twelve in new[] { false, true })
                foreach (bool buffered in new[] { true, false })
                {
                    var result = buffered ? ExecuteBufferedSat(operation, twelve, token) : ExecuteUsbSat(operation, twelve, token);
                    best ??= result;
                    if (result.Succeeded) return result;
                    if (result.Outcome is AtaOutcome.AccessDenied or AtaOutcome.Disconnected or AtaOutcome.IdentityMismatch) return result;
                    best = result;
                }
                if (selected.BridgeChip?.Contains("JMicron", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var vendor = ExecuteJmicron(operation, token);
                    if (vendor.Succeeded || vendor.Outcome is AtaOutcome.AccessDenied or AtaOutcome.Disconnected or AtaOutcome.IdentityMismatch) return vendor;
                    best = vendor;
                }
                return best!;
            }
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
        private AtaCommandResult ExecuteUsbSat(AtaOperation operation, bool twelveByte, CancellationToken token)
        {
            Revalidate(token);
            int size = Marshal.SizeOf<ScsiPassThroughDirect>();
            const int senseLength = 32;
            int transfer = AtaCommandPolicy.TransferLength(operation);
            IntPtr allocation = Marshal.AllocHGlobal(512 + 4095);
            IntPtr data = new((allocation.ToInt64() + 4095) & ~4095L);
            IntPtr header = Marshal.AllocHGlobal(size + senseLength);
            try
            {
                Marshal.Copy(new byte[512], 0, data, 512);
                Marshal.Copy(new byte[size + senseLength], 0, header, size + senseLength);
                var cdb = SatCommandPolicy.Request(operation, twelveByte);
                var paddedCdb = new byte[16]; cdb.CopyTo(paddedCdb, 0);
                var request = new ScsiPassThroughDirect
                {
                    Length = (ushort)size, CdbLength = (byte)cdb.Length,
                    DataIn = transfer == 0 ? (byte)2 : (byte)1, DataTransferLength = (uint)transfer,
                    TimeOutValue = 10, DataBuffer = transfer == 0 ? IntPtr.Zero : data,
                    SenseInfoOffset = (uint)size, SenseInfoLength = senseLength, Cdb = paddedCdb
                };
                Marshal.StructureToPtr(request, header, false);
                token.ThrowIfCancellationRequested();
                Interlocked.Increment(ref dispatchCount);
                bool success = StorageNative.DeviceIoControlPointer(handle, 0x0004D014, header, (uint)(size + senseLength),
                    header, (uint)(size + senseLength), out uint returned, IntPtr.Zero);
                int error = success ? 0 : Marshal.GetLastWin32Error();
                token.ThrowIfCancellationRequested();
                if (!success) return new(operation, error is 1 or 50 or 87 ? AtaOutcome.Unsupported : AtaCommandPolicy.ErrorOutcome(error),
                    $"USB SAT({cdb.Length}): {AtaCommandPolicy.ErrorExplanation(error)}", [], [], DateTimeOffset.UtcNow, error);
                if (returned < size || returned > size + senseLength)
                    return AtaCommandResult.Missing(operation, AtaOutcome.InvalidResponse, "USB SAT response header length is invalid.");
                var response = Marshal.PtrToStructure<ScsiPassThroughDirect>(header);
                if (response.SenseInfoLength > senseLength || size + response.SenseInfoLength > returned)
                    return AtaCommandResult.Missing(operation, AtaOutcome.InvalidResponse, "USB SAT sense length is invalid.");
                var sense = new byte[response.SenseInfoLength]; Marshal.Copy(IntPtr.Add(header, size), sense, 0, sense.Length);
                var payload = new byte[transfer]; Marshal.Copy(data, payload, 0, transfer);
                return SatCommandPolicy.Validate(operation, response.ScsiStatus, payload, response.DataTransferLength, sense);
            }
            finally { Marshal.FreeHGlobal(header); Marshal.FreeHGlobal(allocation); }
        }
        private AtaCommandResult ExecuteBufferedSat(AtaOperation operation, bool twelveByte, CancellationToken token)
        {
            Revalidate(token);
            var cdb = SatCommandPolicy.Request(operation, twelveByte);
            int transfer = AtaCommandPolicy.TransferLength(operation);
            const int header = 56, senseLength = 32, senseOffset = 56;
            int dataOffset = senseOffset + senseLength;
            var buffer = new byte[dataOffset + transfer];
            BitConverter.GetBytes((ushort)header).CopyTo(buffer, 0);
            buffer[6] = (byte)cdb.Length;
            buffer[7] = senseLength;
            buffer[8] = transfer == 0 ? (byte)2 : (byte)1;
            BitConverter.GetBytes(transfer).CopyTo(buffer, 12);
            BitConverter.GetBytes(10u).CopyTo(buffer, 16);
            BitConverter.GetBytes((ulong)dataOffset).CopyTo(buffer, 24);
            BitConverter.GetBytes(senseOffset).CopyTo(buffer, 32);
            cdb.CopyTo(buffer.AsSpan(36));
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref dispatchCount);
            bool success = StorageNative.DeviceIoControl(handle, 0x0004D004, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out uint returned, IntPtr.Zero);
            int error = success ? 0 : Marshal.GetLastWin32Error();
            if (!success) return new(operation, error is 1 or 50 or 87 ? AtaOutcome.Unsupported : AtaCommandPolicy.ErrorOutcome(error),
                $"USB SAT buffered 0x4D004 ({cdb.Length}): {AtaCommandPolicy.ErrorExplanation(error)}", [], [], DateTimeOffset.UtcNow, error);
            if (returned < header) return AtaCommandResult.Missing(operation, AtaOutcome.InvalidResponse, "USB buffered SAT response was truncated.");
            int senseCount = Math.Min((int)buffer[7], senseLength);
            var sense = buffer.AsSpan(senseOffset, senseCount).ToArray();
            var payload = buffer.AsSpan(dataOffset, transfer).ToArray();
            uint moved = BitConverter.ToUInt32(buffer, 12);
            return SatCommandPolicy.Validate(operation, buffer[2], payload, moved == 0 && transfer > 0 && payload.Any(b => b != 0) ? (uint)transfer : moved, sense);
        }
        private AtaCommandResult ExecuteJmicron(AtaOperation operation, CancellationToken token)
        {
            Revalidate(token);
            var cdb = SatCommandPolicy.JmicronRead(operation);
            int transfer = AtaCommandPolicy.TransferLength(operation);
            const int header = 56, senseLength = 32, senseOffset = 56;
            int dataOffset = senseOffset + senseLength;
            var buffer = new byte[dataOffset + transfer];
            BitConverter.GetBytes((ushort)header).CopyTo(buffer, 0);
            buffer[6] = (byte)cdb.Length;
            buffer[7] = senseLength;
            buffer[8] = transfer == 0 ? (byte)2 : (byte)1;
            BitConverter.GetBytes(transfer).CopyTo(buffer, 12);
            BitConverter.GetBytes(10u).CopyTo(buffer, 16);
            BitConverter.GetBytes((ulong)dataOffset).CopyTo(buffer, 24);
            BitConverter.GetBytes(senseOffset).CopyTo(buffer, 32);
            cdb.CopyTo(buffer.AsSpan(36));
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref dispatchCount);
            bool success = StorageNative.DeviceIoControl(handle, 0x0004D004, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out uint returned, IntPtr.Zero);
            int error = success ? 0 : Marshal.GetLastWin32Error();
            if (!success) return new(operation, AtaCommandPolicy.ErrorOutcome(error), $"JMicron 0xDF: {AtaCommandPolicy.ErrorExplanation(error)}", [], [], DateTimeOffset.UtcNow, error);
            if (returned < header) return AtaCommandResult.Missing(operation, AtaOutcome.InvalidResponse, "JMicron 0xDF response was truncated.");
            var sense = buffer.AsSpan(senseOffset, Math.Min((int)buffer[7], senseLength)).ToArray();
            var payload = transfer == 0 ? [] : buffer.AsSpan(dataOffset, transfer).ToArray();
            uint moved = BitConverter.ToUInt32(buffer, 12);
            return SatCommandPolicy.Validate(operation, buffer[2], payload, moved == 0 && transfer > 0 && payload.Any(b => b != 0) ? (uint)transfer : moved, sense);
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
                var cdb = SatCommandPolicy.Request(AtaOperation.SmartData, false);
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
