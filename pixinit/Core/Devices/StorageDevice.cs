namespace pixinit.Core.Devices;

public enum StorageProtocol { Unknown, Sata, Nvme }
public enum ConnectionBus { Unknown, Sata, Nvme, Usb, Raid, Ata, Scsi, Sas, Spaces, Virtual, FileBackedVirtual, Other }
public enum StorageMedia { Unknown, Ssd, Hdd }
[Flags]
public enum DiagnosticCapabilities { None = 0, Identity = 1, SataSmart = 2, NvmeHealth = 4 }
public enum DeviceAccess { Unknown, Available, Limited, AccessDenied }

// Heuristic navigation does not authorize native diagnostic commands.
public sealed record StorageDevice(string Id, string Model, string? Serial,
    StorageProtocol Protocol, ConnectionBus Bus, DiagnosticCapabilities Capabilities,
    DeviceAccess Access, bool IsUnambiguousSystemDisk = false)
{
    public bool HasStorageOverview => CapacityBytes > 0 && Bus is not (ConnectionBus.Virtual or ConnectionBus.FileBackedVirtual);
    public bool ProtocolHeuristic { get; init; }
    public bool MediaInferred { get; init; }
    public string MediaDisplay => Media == StorageMedia.Ssd ? "SSD (reported)" : Media == StorageMedia.Hdd ? "HDD (reported)" : MediaInferred ? "SSD (inferred)" : Media.ToString();
    public string ProtocolBadge => $"{Protocol}{(Bus == ConnectionBus.Raid ? " via RAID controller" : Bus == ConnectionBus.Usb ? " via USB" : "")}{(ProtocolHeuristic ? " - Heuristic detection" : "")}";
    public string AccessBadge => ProtocolHeuristic ? "Limited access | Health not assessed" : $"{Access} | Health not queried";
    public string SerialSuffix => string.IsNullOrWhiteSpace(Serial) ? "Serial unavailable" : $"Serial ending {Serial.Trim().TrimEnd('.')[^Math.Min(4, Serial.Trim().TrimEnd('.').Length)..]}";
    public byte[] RaidIdentifyRaw { get; init; } = [];
    public bool RaidProtocolVerified { get; init; }
    public string ProtocolEvidence { get; init; } = "";
    // A heuristic protocol classification may attempt the same read-only query path.
    // The provider still revalidates this disk's metadata and reports unsupported RAID passthrough.
    public bool CanQuerySata => Protocol == StorageProtocol.Sata && ((Bus == ConnectionBus.Sata && NativeBusType == 11) || (Bus == ConnectionBus.Raid && NativeBusType == 8 && (RaidProtocolVerified || ProtocolHeuristic)));
    public bool CanQueryNvme => Protocol == StorageProtocol.Nvme && ((Bus == ConnectionBus.Nvme && NativeBusType == 17) || (Bus == ConnectionBus.Raid && NativeBusType == 8 && (RaidProtocolVerified || ProtocolHeuristic)));
    public string? WindowsFriendlyName { get; init; }
    public string? WindowsSerial { get; init; }
    public string? WindowsFirmware { get; init; }
    public long? WindowsCapacityBytes { get; init; }
    public string? WindowsAccessState { get; init; }
    public string? WindowsHealthStatus { get; init; }
    public int? DiskNumber { get; init; }
    public string? InterfacePath { get; init; }
    public string? InstanceId { get; init; }
    public int? PcieGeneration { get; init; }
    public int? PcieLaneWidth { get; init; }
    public string? PcieLinkSource { get; init; }
    public string PcieLinkDisplay => PcieGeneration is int gen && PcieLaneWidth is int width ? $"PCIe {gen}.0 x{width}" : "Link not reported by Windows";
    public string? Firmware { get; init; }
    public long? CapacityBytes { get; init; }
    public StorageMedia Media { get; init; }
    public bool? IncursSeekPenalty { get; init; }
    public bool? Removable { get; init; }
    public uint? NativeBusType { get; init; }
    public IReadOnlyList<string> MountPoints { get; init; } = [];
    public string Source { get; init; } = "Not yet queried";
    public string Limitations { get; init; } = "";
    public string IdentityEvidence { get; init; } = "Session identity only";
    public DateTimeOffset? ObservedAt { get; init; }
    public string NavigationCapacity => CapacityBytes is long bytes ? $"{bytes / 1_000_000_000d:N1} GB / {bytes / 1_073_741_824d:N1} GiB" : "Capacity unavailable";
    public string CapacityDisplay => CapacityBytes is long bytes ? $"{bytes / 1_000_000_000d:N1} GB" : "Capacity unavailable";
    public string DisplayName => $"{(DiskNumber is int n ? $"Disk {n}" : "Disk number unavailable")} · {(string.IsNullOrWhiteSpace(Model) ? "Model unavailable" : Model)} · {CapacityDisplay}";
    public string DiscoverySummary => $"Reported bus: {Bus}{(RaidProtocolVerified ? " (via pass-through)" : "")} · Protocol: {Protocol} · Media: {MediaDisplay} · Access: {Access}. {Limitations}";
}
