using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Logging;
using pixinit.Infrastructure.Windows.Interop;

namespace pixinit.Infrastructure.Windows.Sata;

// Published CSMI STP passthrough (signature CSMISAS, control code 25) over IOCTL_SCSI_MINIPORT.
// Only IDENTIFY DEVICE and SMART READ DATA are sent. No writes, no SMART enable, no guessed vendor opcodes.
internal static class IntelRstSmart
{
    internal static byte[]? TryReadSmart(StorageDevice device, CancellationToken token)
    {
        if (device.DiskNumber is not int disk || string.IsNullOrWhiteSpace(device.Serial)) return null;
        foreach (string path in new[] { $@"\\.\PhysicalDrive{disk}", @"\\.\Scsi0:", @"\\.\Scsi1:", @"\\.\Scsi2:" })
        {
            token.ThrowIfCancellationRequested();
            using var handle = StorageNative.CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                DiscoveryLog.Write($"Probe d IOCTL_SCSI_MINIPORT: disk={disk}; path={path}; open failed; error=0x{Marshal.GetLastWin32Error():X8}.");
                continue;
            }
            if (!DriverPresent(handle, out string detail))
            {
                DiscoveryLog.Write($"Probe d IOCTL_SCSI_MINIPORT: disk={disk}; path={path}; CSMI driver info unavailable; {detail}.");
                continue;
            }
            for (byte phy = 0; phy < 8; phy++)
            {
                token.ThrowIfCancellationRequested();
                var identify = Passthrough(handle, phy, identify: true);
                if (identify is null) continue;
                string? serial = null;
                try { serial = AtaIdentifyParser.Parse(identify).Serial; }
                catch (InvalidDataException) { serial = null; }
                if (string.IsNullOrWhiteSpace(serial) || !serial.Trim().EndsWith(device.Serial.Trim()[^Math.Min(4, device.Serial.Trim().Length)..], StringComparison.OrdinalIgnoreCase)) continue;
                var smart = Passthrough(handle, phy, identify: false);
                DiscoveryLog.Write($"Probe d IOCTL_SCSI_MINIPORT: disk={disk}; path={path}; phy={phy}; serial ending matched; SMART={(smart is null ? "unavailable" : "512 bytes")}.");
                if (smart is not null) return smart;
            }
            DiscoveryLog.Write($"Probe d IOCTL_SCSI_MINIPORT: disk={disk}; path={path}; CSMI present; no phy matched the selected serial.");
        }
        return null;
    }

    private static bool DriverPresent(SafeFileHandle handle, out string detail)
    {
        var buffer = new byte[28 + 176];
        WriteHeader(buffer, 1, 176);
        if (!StorageNative.DeviceIoControl(handle, 0x0004D008, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out _, IntPtr.Zero))
        {
            detail = $"error=0x{Marshal.GetLastWin32Error():X8}";
            return false;
        }
        uint code = BitConverter.ToUInt32(buffer, 20);
        detail = $"return=0x{code:X8}";
        return code == 0 && Encoding.ASCII.GetString(buffer, 4, 7) == "CSMISAS";
    }

    private static byte[]? Passthrough(SafeFileHandle handle, byte phy, bool identify)
    {
        const int dataOffset = 164;
        var buffer = new byte[dataOffset + 512];
        WriteHeader(buffer, 25, 136 + 512);
        buffer[120] = phy;
        buffer[121] = phy;
        buffer[136] = 0x27;
        buffer[137] = 0x80;
        buffer[139] = 1;
        buffer[143] = 0xA0;
        if (identify) buffer[144] = 0xEC;
        else
        {
            buffer[138] = 0xD0;
            buffer[141] = 0x4F;
            buffer[142] = 0xC2;
            buffer[144] = 0xB0;
        }
        BitConverter.GetBytes(0x11u).CopyTo(buffer, 156); // PIO read
        BitConverter.GetBytes(512u).CopyTo(buffer, 160);
        if (!StorageNative.DeviceIoControl(handle, 0x0004D008, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out uint returned, IntPtr.Zero))
            return null;
        if (BitConverter.ToUInt32(buffer, 20) != 0 || returned < dataOffset + 512) return null;
        var data = buffer.AsSpan(dataOffset, 512).ToArray();
        return data.All(b => b == 0) ? null : data;
    }

    private static void WriteHeader(byte[] buffer, uint control, uint length)
    {
        BitConverter.GetBytes(28u).CopyTo(buffer, 0);
        "CSMISAS\0"u8.CopyTo(buffer.AsSpan(4));
        BitConverter.GetBytes(60u).CopyTo(buffer, 12);
        BitConverter.GetBytes(control).CopyTo(buffer, 16);
        BitConverter.GetBytes(length).CopyTo(buffer, 24);
    }
}
