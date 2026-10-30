using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Nvme;
using pixinit.Core.Diagnostics.Sata;
using pixinit.Infrastructure.Logging;
using pixinit.Infrastructure.Windows.Interop;
using pixinit.Infrastructure.Windows.Nvme;
using pixinit.Infrastructure.Windows.Sata;

namespace pixinit.Infrastructure.Windows.Discovery;

internal static class StorageQueryService
{
    internal static StorageDevice ProbeRaid(StorageDevice device, Func<uint, byte[]?, int, byte[]> query, CancellationToken token)
    {
        if (device.Bus != ConnectionBus.Raid || device.NativeBusType != 8) return device;
        var notes = new List<string>();
        bool denied = false;
        StorageDevice? Attempt(string name, Func<StorageDevice> action)
        {
            token.ThrowIfCancellationRequested();
            string serialSuffix = string.IsNullOrWhiteSpace(device.Serial) ? "unknown" : device.Serial[^Math.Min(4, device.Serial.Length)..];
            string context = $"disk={device.DiskNumber}; serial ending={serialSuffix}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; protocol attempted={name}";
            try { var result = action(); DiscoveryLog.Write($"RAID pass-through probe: {context}; result=success; error code=none"); return result; }
            catch (Exception ex) when (ex is Win32Exception or InvalidDataException or OverflowException)
            {
                denied |= ex is Win32Exception { NativeErrorCode: 5 };
                notes.Add($"{name}: {ex.Message}");
                DiscoveryLog.Write($"RAID pass-through probe: {context}; result=failed; error code={(ex is Win32Exception win32 ? win32.NativeErrorCode.ToString() : "none")}", ex);
                return null;
            }
        }
        byte[] Property(uint protocol, int length, uint subvalue)
        {
            var request = new byte[48 + length];
            void W(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(offset), value);
            W(0, 50); W(8, protocol); W(12, 1); W(16, subvalue); W(24, 40); W(28, (uint)length);
            var response = query(StorageNative.QueryProperty, request, request.Length);
            DiscoveryLog.Write($"RAID protocol property: disk={device.DiskNumber}; protocol={protocol}; bytes={response.Length}; raw descriptor header={Convert.ToHexString(response.AsSpan(0, Math.Min(48, response.Length)))}");
            uint R(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(offset));
            if (response.Length < 48 || R(0) != 48 || R(4) != 48 || R(8) != protocol || R(12) != 1 || R(24) < 40 || R(28) != length || 8UL + R(24) + R(28) > (ulong)response.Length)
                throw new InvalidDataException("Invalid device-scoped protocol Identify descriptor.");
            var payload = response.AsSpan(checked(8 + (int)R(24)), length).ToArray();
            var preview = payload.AsSpan(0, Math.Min(128, payload.Length)).ToArray();
            preview.AsSpan(protocol == 3 ? 4 : 20, 20).Clear();
            DiscoveryLog.Write($"RAID Identify raw preview (serial bytes zeroed): disk={device.DiskNumber}; protocol={protocol}; hex={Convert.ToHexString(preview)}");
            return payload;
        }
        StorageDevice Ata(byte[] payload)
        {
            var identity = AtaIdentifyParser.Parse(payload);
            if (string.IsNullOrWhiteSpace(device.Serial) || string.IsNullOrWhiteSpace(identity.Serial) || device.Serial.Trim() != identity.Serial.Trim())
                throw new InvalidDataException("ATA Identify serial cannot be attributed to the selected RAID disk.");
            if (string.IsNullOrWhiteSpace(identity.Model) || identity.SataCapabilities is not ushort sata || (sata & 0xE) == 0)
                throw new InvalidDataException("ATA Identify does not establish a SATA interface.");
            return device with { Protocol = StorageProtocol.Sata, RaidProtocolVerified = true,
                Media = identity.RotationRate == 1 ? StorageMedia.Ssd : StorageMedia.Unknown,
                RaidIdentifyRaw = payload.ToArray(), ProtocolEvidence = "Validated device-scoped ATA Identify with SATA capabilities; rotation word retained in Identify data" };
        }
        var established = Attempt("NVMe Identify device property", () =>
        {
            var payload = Property(3, 4096, 1);
            var identity = NvmeControllerParser.Parse(payload);
            // A RAID controller may return adapter data for several disks. Require matching serial attribution.
            if (string.IsNullOrWhiteSpace(device.Serial) || string.IsNullOrWhiteSpace(identity.Serial) || device.Serial.Trim() != identity.Serial.Trim() || string.IsNullOrWhiteSpace(identity.Model))
                throw new InvalidDataException("NVMe Identify serial cannot be attributed to the selected RAID disk.");
            return device with { Protocol = StorageProtocol.Nvme, RaidProtocolVerified = true,
                RaidIdentifyRaw = payload.ToArray(), ProtocolEvidence = "Validated device-scoped NVMe Identify with matching discovery serial; RAID driver scope remains visible" };
        });
        established ??= Attempt("ATA Identify device property", () => Ata(Property(2, 512, 0)));
        established ??= Attempt("ATA Identify pass-through", () =>
        {
            var request = AtaCommandPolicy.Request(AtaOperation.Identify);
            var response = query(0x0004D02C, request, request.Length);
            var result = AtaCommandPolicy.ValidateResponse(AtaOperation.Identify, response, (uint)response.Length, true, 0);
            if (!result.Succeeded) throw new InvalidDataException(result.Explanation);
            return Ata(result.Payload);
        });
        return established is not null ? established with { Access = device.Access == DeviceAccess.AccessDenied ? device.Access : DeviceAccess.Available,
            Limitations = device.Limitations + " Underlying protocol established via read-only RAID probing. SMART support still depends on the RAID driver." }
            : device with { Access = denied ? DeviceAccess.AccessDenied : device.Access, Limitations = device.Limitations + (denied ? " Run as administrator required for RAID mode; driver access may remain restricted even when elevated." : "") + " RAID driver did not provide attributable protocol evidence. " + string.Join(" ", notes) };
    }
}
