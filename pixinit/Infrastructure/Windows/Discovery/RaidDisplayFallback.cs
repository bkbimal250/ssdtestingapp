using System.Runtime.InteropServices;
using pixinit.Core.Devices;
using pixinit.Infrastructure.Logging;

namespace pixinit.Infrastructure.Windows.Discovery;

internal sealed record PhysicalDiskEvidence(string? Serial, long Size, uint Bus, uint Media, string? DeviceId, string? FriendlyName = null, string? Firmware = null, string? OperationalStatus = null, string? HealthStatus = null);
internal static class RaidDisplayFallback
{
    internal static byte[]? ReadFailurePredictSmart(StorageDevice device, CancellationToken token)
    {
        object? locator = null, service = null, rows = null;
        try
        {
            var type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type is null) return null;
            locator = Activator.CreateInstance(type);
            service = ((dynamic)locator!).ConnectServer(".", @"root\WMI");
            rows = ((dynamic)service).ExecQuery("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData");
            var matches = new List<byte[]>();
            foreach (object row in (System.Collections.IEnumerable)rows)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    string? instance = Convert.ToString(WmiValue.Read(row, "InstanceName"));
                    if (!MatchesInstance(device, instance)) continue;
                    if (WmiValue.Read(row, "VendorSpecific") is byte[] bytes && bytes.Length == 512) matches.Add(bytes.ToArray());
                }
                finally { Release(row); }
            }
            byte[]? result = matches.Count == 1 ? matches[0] : null;
            DiscoveryLog.Write($"RAID SMART fallback WMI: disk={device.DiskNumber}; serial ending={SerialSuffix(device.Serial)}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; Protocol attempted=MSStorageDriver_FailurePredictData; SMART passthrough result={(result is null ? "Unavailable/unmatched" : "Data returned")}; error code=none; records matched={matches.Count}.");
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            DiscoveryLog.Write($"RAID SMART fallback WMI: disk={device.DiskNumber}; serial ending={SerialSuffix(device.Serial)}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; Protocol attempted=MSStorageDriver_FailurePredictData; SMART passthrough result=failed; error code=none", ex);
            return null;
        }
        finally { Release(rows); Release(service); Release(locator); }
    }
    internal static void LogPhysicalDiskHealth(StorageDevice device) =>
        DiscoveryLog.Write($"RAID SMART fallback Windows health: disk={device.DiskNumber}; serial ending={SerialSuffix(device.Serial)}; BusType={device.NativeBusType?.ToString() ?? device.Bus.ToString()}; MediaType={device.Media}; IsHeuristic={device.ProtocolHeuristic}; Protocol attempted=MSFT_PhysicalDisk health; SMART passthrough result={(string.IsNullOrWhiteSpace(device.WindowsHealthStatus) ? "health unavailable" : device.WindowsHealthStatus)}; error code=none; fallback source=Windows Storage API; OperationalStatus={device.WindowsAccessState ?? "N/A"}.");
    private static bool MatchesInstance(StorageDevice device, string? instance)
    {
        if (string.IsNullOrWhiteSpace(instance)) return false;
        static string Normalize(string value) => value.Trim().Replace('/', '\\').ToUpperInvariant();
        string candidate = Normalize(instance), pnp = Normalize(device.InstanceId ?? ""), serial = Normalize(device.Serial ?? "");
        return (pnp.Length > 0 && (candidate == pnp || candidate.StartsWith(pnp + "_", StringComparison.Ordinal) || pnp.StartsWith(candidate + "_", StringComparison.Ordinal))) ||
            (serial.Length >= 4 && candidate.Contains(serial, StringComparison.Ordinal));
    }
    private static string SerialSuffix(string? serial)
    {
        string value = (serial ?? "").Trim().TrimEnd('.');
        return value.Length == 0 ? "unknown" : value[^Math.Min(4, value.Length)..];
    }
    internal static IReadOnlyList<StorageDevice> Apply(IReadOnlyList<StorageDevice> devices, IReadOnlyList<PhysicalDiskEvidence> evidence)
    {
        return devices.Select(d =>
        {
            var diskMatches = evidence.Where(e => d.DiskNumber is not null && e.DeviceId == d.DiskNumber.Value.ToString()).ToArray();
            if (diskMatches.Length != 1) diskMatches = evidence.Where(e => e.Size == d.CapacityBytes && !string.IsNullOrWhiteSpace(d.Serial) && string.Equals(d.Serial.Trim(), e.Serial?.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            var diskInfo = diskMatches.Length == 1 ? diskMatches[0] : null;
            string serialForLog = NormalizeSerial(d.Serial ?? diskInfo?.Serial ?? "") ?? "";
            string serialSuffix = serialForLog.Length == 0 ? "unknown" : serialForLog[^Math.Min(4, serialForLog.Length)..];
            string normalizedFallbackSerial = NormalizeSerial(diskInfo?.Serial) ?? "";
            string fallbackSerial = normalizedFallbackSerial.Length == 0 ? "N/A" : normalizedFallbackSerial[^Math.Min(4, normalizedFallbackSerial.Length)..];
            string fallbackSource = diskInfo is null ? "MSFT_PhysicalDisk unavailable/unmatched" : "MSFT_PhysicalDisk";
            if ((d.Bus != ConnectionBus.Raid && d.Bus != ConnectionBus.Usb) || d.Protocol != StorageProtocol.Unknown || d.Access == DeviceAccess.AccessDenied)
            {
                LogInventory(d, diskInfo, d.Protocol, d.ProtocolHeuristic, fallbackSource, serialSuffix, fallbackSerial);
                return diskInfo is null ? d : d with { WindowsFriendlyName = diskInfo.FriendlyName, WindowsSerial = NormalizeSerial(diskInfo.Serial), WindowsFirmware = diskInfo.Firmware, WindowsCapacityBytes = diskInfo.Size > 0 ? diskInfo.Size : null, WindowsAccessState = diskInfo.OperationalStatus, WindowsHealthStatus = diskInfo.HealthStatus };
            }
            var info = diskInfo;
            StorageProtocol protocol = info?.Bus switch { 17 => StorageProtocol.Nvme, 11 => StorageProtocol.Sata, _ => StorageProtocol.Unknown };
            string modelEvidence = $"{d.Model} {info?.FriendlyName}";
            if (protocol == StorageProtocol.Unknown && modelEvidence.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) protocol = StorageProtocol.Nvme;
            else if (protocol == StorageProtocol.Unknown && modelEvidence.Contains("SATA", StringComparison.OrdinalIgnoreCase)) protocol = StorageProtocol.Sata;
            if (protocol == StorageProtocol.Unknown && (info?.Media == 4 || d.Media == StorageMedia.Ssd) && d.IncursSeekPenalty == false)
            {
                // RAID does not disclose the transport here. Keep the disk in Diagnostics with
                // an explicitly heuristic SATA route so the read-only ATA attempt can report support.
                protocol = StorageProtocol.Sata;
            }
            if (protocol == StorageProtocol.Unknown)
            {
                LogInventory(d, info, protocol, d.ProtocolHeuristic, fallbackSource, serialSuffix, fallbackSerial);
                return info is null ? d : d with { Media = info.Media switch { 4 => StorageMedia.Ssd, 3 => StorageMedia.Hdd, _ => d.Media },
                    ProtocolEvidence = "Windows Storage reports media type; underlying ATA/NVMe protocol remains unconfirmed." };
            }
            string source = info is null ? "Windows discovery" : "MSFT_PhysicalDisk and Windows discovery";
            LogInventory(d, info, protocol, isHeuristic: true, fallbackSource, serialSuffix, fallbackSerial);
            DiscoveryLog.Write($"{d.Bus} display fallback: disk={d.DiskNumber}; serial ending {serialSuffix}; protocol={protocol}; source={source}; heuristic query enabled.");
            return d with { Protocol = protocol, ProtocolHeuristic = true, Media = info?.Media == 4 ? StorageMedia.Ssd : d.Media,
                Model = string.IsNullOrWhiteSpace(info?.FriendlyName) ? d.Model : info.FriendlyName,
                Serial = string.IsNullOrWhiteSpace(info?.Serial) ? d.Serial : NormalizeSerial(info.Serial),
                Firmware = string.IsNullOrWhiteSpace(info?.Firmware) ? d.Firmware : info.Firmware,
                CapacityBytes = info?.Size > 0 ? info.Size : d.CapacityBytes,
                WindowsFriendlyName = info?.FriendlyName, WindowsSerial = NormalizeSerial(info?.Serial), WindowsFirmware = info?.Firmware,
                WindowsCapacityBytes = info?.Size, WindowsAccessState = info?.OperationalStatus,
                WindowsHealthStatus = info?.HealthStatus,
                MediaInferred = info?.Media != 4 && d.IncursSeekPenalty == false,
                Access = DeviceAccess.Limited, ProtocolEvidence = $"{source}: heuristic protocol detection; read-only diagnostic queries may be attempted and may be blocked by the controller.",
                Limitations = $"{d.Bus} controller did not provide attributable Identify data. Limited diagnostic access; results depend on controller passthrough support. See %TEMP%/PIXINIT_detect.log." };
        }).ToArray();
    }
    private static void LogInventory(StorageDevice d, PhysicalDiskEvidence? evidence, StorageProtocol protocol, bool isHeuristic, string fallbackSource, string serialSuffix, string fallbackSerial) =>
        DiscoveryLog.Write($"Disk inventory decision: disk={d.DiskNumber}; serial ending={serialSuffix}; BusType={d.NativeBusType?.ToString() ?? d.Bus.ToString()}; MediaType={evidence?.Media.ToString() ?? d.Media.ToString()}; IsHeuristic={isHeuristic}; Protocol attempted={protocol}; fallback source={fallbackSource}; FriendlyName={evidence?.FriendlyName ?? "N/A"}; Size={evidence?.Size.ToString() ?? "N/A"}; FirmwareVersion={evidence?.Firmware ?? "N/A"}; SerialNumber ending={fallbackSerial}");
    private static string? NormalizeSerial(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('.');
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
            rows = ((dynamic)service).ExecQuery("SELECT SerialNumber, Size, BusType, MediaType, DeviceId, FriendlyName, FirmwareVersion, OperationalStatus, HealthStatus FROM MSFT_PhysicalDisk");
            foreach (object row in (System.Collections.IEnumerable)rows)
            {
                try { token.ThrowIfCancellationRequested(); result.Add(new(Convert.ToString(WmiValue.Read(row, "SerialNumber")), Convert.ToInt64(WmiValue.Read(row, "Size")), Convert.ToUInt32(WmiValue.Read(row, "BusType")), Convert.ToUInt32(WmiValue.Read(row, "MediaType")), Convert.ToString(WmiValue.Read(row, "DeviceId")), Convert.ToString(WmiValue.Read(row, "FriendlyName")), Convert.ToString(WmiValue.Read(row, "FirmwareVersion")), Convert.ToString(WmiValue.Read(row, "OperationalStatus")), Convert.ToString(WmiValue.Read(row, "HealthStatus")))); }
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
