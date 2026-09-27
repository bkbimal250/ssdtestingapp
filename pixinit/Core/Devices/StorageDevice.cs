namespace pixinit.Core.Devices;

public enum StorageProtocol { Unknown, Sata, Nvme }
public enum ConnectionBus { Unknown, Sata, Nvme, Usb, Raid, Ata, Scsi, Sas, Spaces, Virtual, FileBackedVirtual, Other }
public enum StorageMedia { Unknown, Ssd, Hdd }
[Flags]
public enum DiagnosticCapabilities { None = 0, Identity = 1, SataSmart = 2, NvmeHealth = 4 }
public enum DeviceAccess { Unknown, Available, Limited, AccessDenied }

// Protocol must come from authoritative discovery, never model-name heuristics.
public sealed record StorageDevice(string Id, string Model, string? Serial,
    StorageProtocol Protocol, ConnectionBus Bus, DiagnosticCapabilities Capabilities,
    DeviceAccess Access, bool IsUnambiguousSystemDisk = false)
{
    public int? DiskNumber { get; init; }
    public string? InterfacePath { get; init; }
    public string? InstanceId { get; init; }
    public string? Firmware { get; init; }
    public long? CapacityBytes { get; init; }
    public StorageMedia Media { get; init; }
    public bool? Removable { get; init; }
    public uint? NativeBusType { get; init; }
    public IReadOnlyList<string> MountPoints { get; init; } = [];
    public string Source { get; init; } = "Not yet queried";
    public string Limitations { get; init; } = "";
    public string IdentityEvidence { get; init; } = "Session identity only";
    public DateTimeOffset? ObservedAt { get; init; }
    public string CapacityDisplay => CapacityBytes is long bytes ? $"{bytes / 1_000_000_000d:N1} GB" : "Capacity unavailable";
    public string DisplayName => $"{(DiskNumber is int n ? $"Disk {n}" : "Disk number unavailable")} · {(string.IsNullOrWhiteSpace(Model) ? "Model unavailable" : Model)} · {CapacityDisplay}";
    public string DiscoverySummary => $"Reported bus: {Bus} · Media: {Media} · Access: {Access}. {Limitations}";
}
