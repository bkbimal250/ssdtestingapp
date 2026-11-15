using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Logging;
using pixinit.Infrastructure.Windows.Interop;
using pixinit.Infrastructure.Windows.Nvme;
using pixinit.Infrastructure.Windows.Sata;

namespace pixinit.Infrastructure.Windows.Discovery;

// Read-only SCSI INQUIRY and SAT IDENTIFY for USB-SATA bridges. No writes, no SMART enable.
internal static class UsbBridgeProbe
{
    internal static StorageDevice Apply(StorageDevice device, string path, CancellationToken token)
    {
        if (device.Bus != ConnectionBus.Usb || string.IsNullOrWhiteSpace(path)) return device;
        token.ThrowIfCancellationRequested();
        string? chip = null;
        var port = ReadUsbPort(device.InstanceId);
        bool nvmeBridge = port.Vendor == 0x152D && port.Product == 0x0583;
        try
        {
            using var handle = OpenDisk(path);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                DiscoveryLog.Write($"USB bridge probe: disk={device.DiskNumber}; open failed; error=0x{error:X8}; speed={port.Speed}; usb={port.Vendor:X4}:{port.Product:X4}.");
                return nvmeBridge
                    ? device with { UsbSpeed = port.Speed, BridgeChip = "JMicron", Protocol = StorageProtocol.Nvme, ProtocolHeuristic = true, UsbNvmeAdmin = true, InterfaceLabel = "UASP (NVM Express)", Limitations = device.Limitations + " UASP NVMe admin pass-through needs administrator. SAT was not sent." }
                    : device with { UsbSpeed = port.Speed, Limitations = device.Limitations + " USB bridge open failed; run as administrator." };
            }
            chip = Inquiry(handle);
            if (nvmeBridge || chip?.Contains("JMicron", StringComparison.OrdinalIgnoreCase) == true)
            {
                var admin = JmicronNvmeAdmin.ReadIdentify(handle);
                if (admin is null && JmicronNvmeAdmin.LastError == 5)
                {
                    DiscoveryLog.Write($"USB {port.Vendor:X4}:{port.Product:X4} NVMe admin pass-through denied; SAT 0x85/0xA1/0xDF not sent.");
                    return device with { UsbSpeed = port.Speed, BridgeChip = chip ?? "JMicron", Protocol = StorageProtocol.Nvme, ProtocolHeuristic = true, UsbNvmeAdmin = true, InterfaceLabel = "UASP (NVM Express)", Access = DeviceAccess.AccessDenied, Limitations = device.Limitations + " UASP NVMe admin pass-through needs administrator. SAT was not sent." };
                }
                if (admin is not null)
                {
                    try
                    {
                        var controller = NvmeControllerParser.Parse(admin);
                        DiscoveryLog.Write($"USB NVMe admin identify: disk={device.DiskNumber}; bridge={chip ?? "JMicron"}; usb={port.Vendor:X4}:{port.Product:X4}; model={controller.Model}; serial ending={Suffix(controller.Serial)}; firmware={controller.Firmware}; standard={controller.Version >> 16}.{(controller.Version >> 8) & 255}.");
                        return device with
                        {
                            Model = string.IsNullOrWhiteSpace(controller.Model) ? device.Model : controller.Model,
                            Serial = string.IsNullOrWhiteSpace(controller.Serial) ? device.Serial : controller.Serial,
                            Firmware = string.IsNullOrWhiteSpace(controller.Firmware) ? device.Firmware : controller.Firmware,
                            Protocol = StorageProtocol.Nvme,
                            ProtocolHeuristic = false,
                            UsbNvmeAdmin = true,
                            UsbSpeed = port.Speed,
                            BridgeChip = chip ?? "JMicron",
                            InterfaceLabel = "UASP (NVM Express)",
                            Capabilities = DiagnosticCapabilities.Identity | DiagnosticCapabilities.NvmeHealth,
                            ProtocolEvidence = $"UASP (NVM Express). Bridge {chip ?? "JMicron"} USB {port.Vendor:X4}:{port.Product:X4}. NVMe Admin pass-through. " + device.ProtocolEvidence
                        };
                    }
                    catch (Exception ex) { DiscoveryLog.Write("USB NVMe admin identify payload was not a controller identity", ex); }
                }
                DiscoveryLog.Write($"USB JMicron NVMe admin identify failed: disk={device.DiskNumber}; usb={port.Vendor:X4}:{port.Product:X4}; SAT 0x85/0xA1/0xDF not sent.");
                return device with { UsbSpeed = port.Speed, BridgeChip = chip ?? "JMicron", Protocol = StorageProtocol.Nvme, ProtocolHeuristic = true, UsbNvmeAdmin = true, InterfaceLabel = "UASP (NVM Express)", ProtocolEvidence = "UASP (NVM Express). NVMe admin pass-through did not return Identify Controller. SAT was not sent." };
            }
            var identify = SatIdentify(handle, chip);
            DiscoveryLog.Write($"USB bridge probe: disk={device.DiskNumber}; bridge={chip ?? "unidentified"}; speed={port.Speed}; SAT IDENTIFY={(identify is null ? "failed" : "512 bytes")}; discovery serial ending={Suffix(device.Serial)}.");
            if (identify is null) return device with { UsbSpeed = port.Speed, BridgeChip = chip, Protocol = device.Protocol == StorageProtocol.Unknown ? StorageProtocol.Sata : device.Protocol, ProtocolHeuristic = device.Protocol == StorageProtocol.Unknown || device.ProtocolHeuristic };
            AtaIdentity? parsed = null;
            try { parsed = AtaIdentifyParser.Parse(identify); }
            catch (Exception ex) { DiscoveryLog.Write("USB SAT IDENTIFY parsed with the strict checker failed; bridge serial words are still inspected", ex); }
            string? serial = parsed?.Serial ?? LooseText(identify, 10, 10);
            string? model = parsed?.Model ?? LooseText(identify, 27, 20);
            string? firmware = parsed?.Firmware ?? LooseText(identify, 23, 4);
            bool fake = IsBridgeSerial(device.Serial);
            bool replaced = !string.IsNullOrWhiteSpace(serial) && (fake || !string.Equals(serial.Trim(), device.Serial?.Trim(), StringComparison.Ordinal));
            if (replaced) DiscoveryLog.Write($"USB bridge serial replaced: disk={device.DiskNumber}; bridge={chip ?? "unidentified"}; fake={fake}; ATA serial ending={Suffix(serial)}.");
            return device with
            {
                Model = string.IsNullOrWhiteSpace(model) ? device.Model : model,
                Serial = replaced ? serial : device.Serial,
                Firmware = firmware ?? device.Firmware,
                Protocol = StorageProtocol.Sata,
                ProtocolHeuristic = parsed is null,
                UsbSpeed = port.Speed,
                BridgeChip = chip,
                ProtocolEvidence = $"USB bridge {chip ?? "unidentified"} SAT IDENTIFY. " + device.ProtocolEvidence,
                Limitations = device.Limitations + (fake && string.IsNullOrWhiteSpace(serial) ? " Bridge serial could not be replaced from ATA words 10-19." : "")
            };
        }
        catch (Exception ex)
        {
            DiscoveryLog.Write($"USB bridge probe failed: disk={device.DiskNumber}; bridge={chip ?? "unidentified"}", ex);
            return nvmeBridge
                ? device with { UsbSpeed = port.Speed, BridgeChip = chip ?? "JMicron", Protocol = StorageProtocol.Nvme, ProtocolHeuristic = true, UsbNvmeAdmin = true, InterfaceLabel = "UASP (NVM Express)" }
                : device with { UsbSpeed = port.Speed, BridgeChip = chip };
        }
    }

    internal static bool IsBridgeSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial)) return true;
        string compact = new string(serial.Where(char.IsAsciiLetterOrDigit).ToArray());
        return compact.Length == 0 || compact.All(c => c == '0') || compact.Equals("000000000247", StringComparison.Ordinal);
    }

    private static string? Inquiry(SafeFileHandle handle)
    {
        var cdb = new byte[16];
        cdb[0] = 0x12; cdb[4] = 36;
        var data = Buffered(handle, cdb, 6, 36);
        if (data is null || data.Length < 32) return null;
        string vendor = Encoding.ASCII.GetString(data, 8, 8).Trim();
        string product = Encoding.ASCII.GetString(data, 16, 16).Trim();
        string chip = $"{vendor} {product}".Trim();
        return chip.Length == 0 ? null : chip;
    }

    private static byte[]? SatIdentify(SafeFileHandle handle, string? chip)
    {
            if (chip?.Contains("JMicron", StringComparison.OrdinalIgnoreCase) == true) return null;
            foreach (bool twelve in new[] { false, true })
            {
                var cdb = SatCommandPolicy.Request(AtaOperation.Identify, twelve);
                var data = Buffered(handle, cdb, cdb.Length, 512);
                if (data is { Length: 512 } && data.Any(b => b != 0)) return data;
            }
            return null;
    }

    private static byte[]? Buffered(SafeFileHandle handle, byte[] cdb, int cdbLength, int transfer)
    {
        const int header = 56, senseOffset = 56, senseLength = 32;
        int dataOffset = senseOffset + senseLength;
        var buffer = new byte[dataOffset + transfer];
        BitConverter.GetBytes((ushort)header).CopyTo(buffer, 0);
        buffer[6] = (byte)cdbLength;
        buffer[7] = senseLength;
        buffer[8] = 1;
        BitConverter.GetBytes(transfer).CopyTo(buffer, 12);
        BitConverter.GetBytes(10u).CopyTo(buffer, 16);
        BitConverter.GetBytes((ulong)dataOffset).CopyTo(buffer, 24);
        BitConverter.GetBytes(senseOffset).CopyTo(buffer, 32);
        cdb.AsSpan(0, Math.Min(cdb.Length, 16)).CopyTo(buffer.AsSpan(36));
        bool ok = StorageNative.DeviceIoControl(handle, 0x0004D004, buffer, (uint)buffer.Length, buffer, (uint)buffer.Length, out uint returned, IntPtr.Zero);
        if (cdb[0] == 0xDF)
        {
            int available = Math.Max(0, buffer.Length - dataOffset);
            var payload = buffer.AsSpan(dataOffset, Math.Min(transfer, available));
            DiscoveryLog.Write($"JMicron 0xDF identify: cdb={Convert.ToHexString(cdb)}; ok={ok}; error=0x{(ok ? 0 : Marshal.GetLastWin32Error()):X8}; returned={returned}; scsi=0x{buffer[2]:X2}; sense={Convert.ToHexString(buffer.AsSpan(senseOffset, Math.Min(16, Math.Max(0, buffer.Length - senseOffset))))}; nonzero={payload.ContainsAnyExcept((byte)0)}.");
        }
        if (!ok || returned < dataOffset + transfer || buffer[2] is not (0 or 2))
            return null;
        return buffer.AsSpan(dataOffset, transfer).ToArray();
    }

    private static string? LooseText(byte[] payload, int word, int words)
    {
        var bytes = new byte[words * 2];
        for (int i = 0; i < bytes.Length; i += 2)
        {
            int offset = word * 2 + i;
            if (offset + 1 >= payload.Length) return null;
            bytes[i] = payload[offset + 1];
            bytes[i + 1] = payload[offset];
        }
        if (bytes.Any(b => b != 0 && (b < 32 || b > 126))) return null;
        string value = Encoding.ASCII.GetString(bytes).Trim(' ', '\0');
        return value.Length == 0 ? null : value;
    }

    private readonly record struct UsbPort(string Speed, ushort Vendor, ushort Product);
    private static UsbPort Unavailable(string reason) => new(reason, 0, 0);
    private static UsbPort ReadUsbPort(string? instanceId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(instanceId) || CM_Locate_DevNodeW(out uint node, instanceId, 0) != 0) return Unavailable("USB speed unavailable - device node was not found");
            uint portNode = node;
            uint hub = 0;
            for (int i = 0; i < 8 && CM_Get_Parent(out uint parent, portNode, 0) == 0; i++)
            {
                // CM_DRP_SERVICE is 5. Property 4 is unused and never names the hub.
                string service = RegistryText(parent, 5);
                string id = DeviceId(parent);
                if (service.Contains("USBHUB", StringComparison.OrdinalIgnoreCase) || id.Contains("ROOT_HUB", StringComparison.OrdinalIgnoreCase)) { hub = parent; break; }
                portNode = parent;
            }
            if (hub == 0) return Unavailable("USB speed unavailable - parent USB hub was not found - Try a motherboard USB port");
            uint addressLength = 4;
            var address = new byte[4];
            if (CM_Get_DevNode_Registry_PropertyW(portNode, 29, out _, address, ref addressLength, 0) != 0) return Unavailable("USB speed unavailable - hub port number was not readable");
            uint port = BitConverter.ToUInt32(address, 0);
            if (port == 0) return Unavailable("USB speed unavailable - hub port number was zero");
            string hubId = DeviceId(hub);
            string? hubPath = FindHubPath(hubId);
            if (hubPath is null) return Unavailable("USB speed unavailable - hub interface was not opened");
            using var handle = OpenHub(hubPath);
            if (handle.IsInvalid) return Unavailable("USB speed unavailable - hub handle was denied - Run as administrator");
            var info = new byte[256];
            BitConverter.GetBytes(port).CopyTo(info, 0);
            if (!StorageNative.DeviceIoControl(handle, 0x00220448, info, (uint)info.Length, info, (uint)info.Length, out _, IntPtr.Zero))
                return Unavailable($"USB speed unavailable - hub query failed 0x{Marshal.GetLastWin32Error():X8}");
            ushort vendor = info.Length >= 16 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(12)) : (ushort)0;
            ushort product = info.Length >= 18 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(14)) : (ushort)0;
            DiscoveryLog.Write($"USB connection: port={port}; speedByte={info[23]}; usb={vendor:X4}:{product:X4}; descriptor={Convert.ToHexString(info.AsSpan(4, 18))}");
            string speed = info[23] switch
            {
                2 => "USB 2.0 480Mbps - Bottleneck",
                3 => "USB 3.0 5Gbps",
                1 => "USB 1.1 12Mbps - Bottleneck",
                _ => "USB speed unavailable - hub returned an unknown speed"
            };
            return new UsbPort(speed, vendor, product);
        }
        catch (Exception ex)
        {
            DiscoveryLog.Write("USB speed probe failed", ex);
            return Unavailable("USB speed unavailable - hub port was not readable - Try a motherboard USB port");
        }
    }

    private static string? FindHubPath(string hubId)
    {
        var guid = new Guid("F18A0E88-C30C-11D0-8815-00A0C906BED8");
        using var set = StorageNative.SetupDiGetClassDevsW(guid, null, IntPtr.Zero, 0x12);
        if (set.IsInvalid) return null;
        for (uint index = 0; index < 64; index++)
        {
            var data = new StorageNative.InterfaceData { Size = (uint)Marshal.SizeOf<StorageNative.InterfaceData>() };
            if (!StorageNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, guid, index, ref data)) break;
            var info = new StorageNative.DeviceInfo { Size = (uint)Marshal.SizeOf<StorageNative.DeviceInfo>() };
            StorageNative.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint required, ref info);
            if (Marshal.GetLastWin32Error() != 122 || required < 8 || required > 4096) continue;
            var memory = Marshal.AllocHGlobal(checked((int)required));
            try
            {
                Marshal.WriteInt32(memory, 8);
                if (!StorageNative.SetupDiGetDeviceInterfaceDetailW(set, ref data, memory, required, out uint actual, ref info)) continue;
                var id = new StringBuilder(512);
                if (!StorageNative.SetupDiGetDeviceInstanceIdW(set, ref info, id, (uint)id.Capacity, out _) || !id.ToString().Equals(hubId, StringComparison.OrdinalIgnoreCase)) continue;
                var bytes = new byte[actual - 4];
                Marshal.Copy(IntPtr.Add(memory, 4), bytes, 0, bytes.Length);
                return WindowsDiskDiscovery.ReadWidePath(bytes);
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        return null;
    }

    private static SafeFileHandle OpenDisk(string path)
    {
        SafeFileHandle? last = null;
        foreach (uint access in new uint[] { 0xC0000000, 0x80000000, 0 })
        {
            var handle = StorageNative.CreateFileW(path, access, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (!handle.IsInvalid) { last?.Dispose(); return handle; }
            last?.Dispose();
            last = handle;
        }
        return last ?? StorageNative.CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    }

    private static SafeFileHandle OpenHub(string hubPath)
    {
        foreach (uint access in new uint[] { 0x80000000, 0 })
        {
            var handle = StorageNative.CreateFileW(hubPath, access, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            handle.Dispose();
        }
        return StorageNative.CreateFileW(hubPath, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
    }

    private static string DeviceId(uint node)
    {
        var buffer = new StringBuilder(512);
        return CM_Get_Device_IDW(node, buffer, buffer.Capacity, 0) == 0 ? buffer.ToString() : "";
    }

    private static string RegistryText(uint node, int property)
    {
        uint length = 512;
        var buffer = new byte[length];
        if (CM_Get_DevNode_Registry_PropertyW(node, property, out _, buffer, ref length, 0) != 0 || length < 2) return "";
        return Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(length, (uint)buffer.Length)).TrimEnd('\0');
    }

    private static string Suffix(string? serial)
    {
        string value = (serial ?? "").Trim();
        return value.Length == 0 ? "unknown" : value[^Math.Min(4, value.Length)..];
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out uint node, string id, int flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint node, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_IDW(uint node, StringBuilder id, int length, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_DevNode_Registry_PropertyW(uint node, int property, out uint type, byte[] buffer, ref uint length, int flags);
}
