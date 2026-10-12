using System.Runtime.InteropServices;
using pixinit.Core.Devices;
using pixinit.Infrastructure.Logging;

namespace pixinit.Infrastructure.Windows.Discovery;

internal sealed record PhysicalDiskEvidence(string? Serial, long Size, uint Bus, uint Media, string? DeviceId, string? FriendlyName = null);
internal static class RaidDisplayFallback
{
    internal static IReadOnlyList<StorageDevice> Apply(IReadOnlyList<StorageDevice> devices, IReadOnlyList<PhysicalDiskEvidence> evidence)
    {
        return devices.Select(d =>
        {
            if ((d.Bus != ConnectionBus.Raid && d.Bus != ConnectionBus.Usb) || d.Protocol != StorageProtocol.Unknown || d.Access == DeviceAccess.AccessDenied) return d;
            var matches = evidence.Where(e => e.Size == d.CapacityBytes &&
                ((!string.IsNullOrWhiteSpace(d.Serial) && string.Equals(d.Serial.Trim(), e.Serial?.Trim(), StringComparison.OrdinalIgnoreCase)) || (d.DiskNumber is not null && e.DeviceId == d.DiskNumber.Value.ToString()))).ToArray();
            var info = matches.Length == 1 ? matches[0] : null;
            StorageProtocol protocol = info?.Bus switch { 17 => StorageProtocol.Nvme, 11 => StorageProtocol.Sata, _ => StorageProtocol.Unknown };
            string model = d.Model + " " + info?.FriendlyName;
            if (protocol == StorageProtocol.Unknown && d.IncursSeekPenalty == false)
            {
                if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) protocol = StorageProtocol.Nvme;
                else if (model.Contains("SATA", StringComparison.OrdinalIgnoreCase)) protocol = StorageProtocol.Sata;
            }
            if (protocol == StorageProtocol.Unknown)
                return info is null ? d : d with { Media = info.Media switch { 4 => StorageMedia.Ssd, 3 => StorageMedia.Hdd, _ => d.Media },
                    ProtocolEvidence = "Windows Storage reports media type; underlying ATA/NVMe protocol remains unconfirmed." };
            string source = info is null ? "Windows discovery model name" : "MSFT_PhysicalDisk and Windows discovery";
            DiscoveryLog.Write($"{d.Bus} display fallback: disk={d.DiskNumber}; protocol={protocol}; source={source}; heuristic only, transport not authorized.");
            return d with { Protocol = protocol, ProtocolHeuristic = true, Media = info?.Media == 4 ? StorageMedia.Ssd : d.Media,
                MediaInferred = info?.Media != 4 && d.IncursSeekPenalty == false,
                Access = DeviceAccess.Limited, ProtocolEvidence = $"{source}: heuristic protocol detection, not established by direct Identify. Controller-specific driver rejected standard queries. Diagnostic commands remain unavailable until attributable identity is validated.",
                Limitations = $"{d.Bus} controller did not provide valid attributable Identify data. Limited diagnostic access; see %TEMP%/PIXINIT_detect.log for technical results." };
        }).ToArray();
    }
    internal static IReadOnlyList<PhysicalDiskEvidence> Read(CancellationToken token)
    {
        var result = new List<PhysicalDiskEvidence>();
        object? locator = null, service = null, rows = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type is null) return result;
            locator = Activator.CreateInstance(type);
            service = ((dynamic)locator!).ConnectServer(".", @"root\Microsoft\Windows\Storage");
            rows = ((dynamic)service).ExecQuery("SELECT SerialNumber, Size, BusType, MediaType, DeviceId, FriendlyName FROM MSFT_PhysicalDisk");
            foreach (object row in (System.Collections.IEnumerable)rows)
            {
                try { token.ThrowIfCancellationRequested(); result.Add(new(Convert.ToString(WmiValue.Read(row, "SerialNumber")), Convert.ToInt64(WmiValue.Read(row, "Size")), Convert.ToUInt32(WmiValue.Read(row, "BusType")), Convert.ToUInt32(WmiValue.Read(row, "MediaType")), Convert.ToString(WmiValue.Read(row, "DeviceId")), Convert.ToString(WmiValue.Read(row, "FriendlyName")))); }
                finally { Release(row); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { DiscoveryLog.Write("MSFT_PhysicalDisk inventory unavailable; display fallback retains discovery evidence", ex); }
        finally { Release(rows); Release(service); Release(locator); }
        DiscoveryLog.Write($"Source MSFT_PhysicalDisk completed: {result.Count} records.");
        return result;
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}
