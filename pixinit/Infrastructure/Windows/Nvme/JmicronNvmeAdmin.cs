using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using pixinit.Infrastructure.Logging;
using pixinit.Infrastructure.Windows.Interop;

namespace pixinit.Infrastructure.Windows.Nvme;

// Two-phase NVMe admin SCSI pass-through for a UASP bridge. This is not SAT:
// CDB byte 1 is 0x80 for the admin command and 0x82 for the DMA-in read.
internal static class JmicronNvmeAdmin
{
    internal static int LastError { get; private set; }
    internal static byte[]? ReadIdentify(SafeFileHandle handle)
    {
        byte[] command = new byte[512];
        command[0] = (byte)'N'; command[1] = (byte)'V'; command[2] = (byte)'M'; command[3] = (byte)'E';
        command[8] = 0x06;
        command[0x30] = 0x01;
        foreach (var transfer in new[] { (Length: 4096, Code: (byte)0x10), (Length: 512, Code: (byte)0x02) })
        {
            var payload = Exchange(handle, command, transfer.Length, transfer.Code);
            if (payload is null || !PlausibleIdentify(payload)) continue;
            if (payload.Length == 4096) return payload;
            var padded = new byte[4096];
            payload.AsSpan(0, Math.Min(payload.Length, 4096)).CopyTo(padded);
            return padded;
        }
        return null;
    }

    internal static byte[]? ReadHealth(SafeFileHandle handle)
    {
        byte[] command = new byte[512];
        command[0] = (byte)'N'; command[1] = (byte)'V'; command[2] = (byte)'M'; command[3] = (byte)'E';
        command[8] = 0x02;
        command[10] = 0x56;
        command[12] = command[13] = command[14] = command[15] = 0xFF;
        command[0x21] = 0x40;
        command[0x22] = 0x7A;
        command[0x30] = 0x02;
        command[0x32] = 0x7F;
        var payload = Exchange(handle, command, 512, 0x02);
        if (payload is null || payload.Length < 512 || payload.Take(512).All(b => b == 0)) return null;
        return payload.Length == 512 ? payload : payload.AsSpan(0, 512).ToArray();
    }

    private static bool PlausibleIdentify(byte[] payload)
    {
        int sum = 0;
        int limit = Math.Min(512, payload.Length);
        for (int i = 0; i < limit; i++) sum += payload[i];
        if (sum is 0 or 317) return false;
        int printable = 0;
        for (int i = 4; i < Math.Min(24, payload.Length); i++)
            if (payload[i] is >= 32 and <= 126) printable++;
        return printable >= 4;
    }

    private static byte[]? Exchange(SafeFileHandle handle, byte[] command, int transfer, byte lengthCode)
    {
        LastError = 0;
        Mutex? mutex = null;
        bool held = false;
        try
        {
            try
            {
                mutex = new Mutex(false, @"Global\Access_JMicron_SMART");
                held = mutex.WaitOne(3000);
            }
            catch (AbandonedMutexException) { held = true; }
            catch (Exception ex) { DiscoveryLog.Write("JMicron NVMe admin mutex was not acquired", ex); }
            if (!Phase(handle, command, 512, 0x02, false)) return null;
            return Phase(handle, null, transfer, lengthCode, true) ? ReadBack(handle, transfer, lengthCode) : null;
        }
        finally
        {
            if (held) try { mutex?.ReleaseMutex(); } catch (ApplicationException) { }
            mutex?.Dispose();
        }
    }

    private static byte[]? ReadBack(SafeFileHandle handle, int transfer, byte lengthCode)
    {
        const int senseLength = 24, dataOffset = 80;
        var buffer = new byte[dataOffset + transfer];
        Fill(buffer, senseLength, transfer, lengthCode, 1);
        bool ok = StorageNative.DeviceIoControl(handle, 0x0004D004, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out uint returned, IntPtr.Zero);
        int error = ok ? 0 : Marshal.GetLastWin32Error();
        var payload = buffer.AsSpan(dataOffset, Math.Min(transfer, Math.Max(0, buffer.Length - dataOffset)));
        DiscoveryLog.Write($"JMicron NVMe admin data: code=0x{lengthCode:X2}; bytes={transfer}; ok={ok}; error=0x{error:X8}; returned={returned}; scsi=0x{buffer[2]:X2}; sense={Convert.ToHexString(buffer.AsSpan(56, Math.Min(16, Math.Max(0, buffer.Length - 56))))}; nonzero={payload.ContainsAnyExcept((byte)0)}.");
        if (!ok || Illegal(buffer)) return null;
        return payload.ToArray();
    }

    private static bool Phase(SafeFileHandle handle, byte[]? command, int transfer, byte lengthCode, bool inbound)
    {
        if (inbound) return true;
        const int dataOffset = 80;
        var buffer = new byte[dataOffset + transfer];
        Fill(buffer, 24, transfer, lengthCode, 0);
        if (command is not null) command.AsSpan(0, Math.Min(command.Length, transfer)).CopyTo(buffer.AsSpan(dataOffset));
        bool ok = StorageNative.DeviceIoControl(handle, 0x0004D004, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out uint returned, IntPtr.Zero);
        int error = ok ? 0 : Marshal.GetLastWin32Error();
        DiscoveryLog.Write($"JMicron NVMe admin command: code=0x{lengthCode:X2}; ok={ok}; error=0x{error:X8}; returned={returned}; scsi=0x{buffer[2]:X2}; sense={Convert.ToHexString(buffer.AsSpan(56, Math.Min(16, Math.Max(0, buffer.Length - 56))))}.");
        if (!ok) LastError = error;
        return ok && !Illegal(buffer);
    }

    private static bool Illegal(byte[] buffer) => buffer.Length > 58 && buffer[2] == 0x02 && (buffer[58] & 0x0F) == 5;

    private static void Fill(byte[] buffer, int senseLength, int transfer, byte lengthCode, byte dataIn)
    {
        BitConverter.GetBytes((ushort)56).CopyTo(buffer, 0);
        buffer[6] = 12;
        buffer[7] = (byte)senseLength;
        buffer[8] = dataIn;
        BitConverter.GetBytes(transfer).CopyTo(buffer, 12);
        BitConverter.GetBytes(2u).CopyTo(buffer, 16);
        BitConverter.GetBytes(80ul).CopyTo(buffer, 24);
        BitConverter.GetBytes(56).CopyTo(buffer, 32);
        buffer[36] = 0xA1;
        buffer[37] = dataIn == 0 ? (byte)0x80 : (byte)0x82;
        buffer[40] = lengthCode;
    }
}
