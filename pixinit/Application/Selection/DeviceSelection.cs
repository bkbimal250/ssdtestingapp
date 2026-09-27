using pixinit.Core.Devices;

namespace pixinit.Application.Selection;

// UI-independent selection policy. Revision protects user intent during discovery.
public sealed class DeviceSelection
{
    private long revision;
    private bool userChoseTab;
    public StorageProtocol ActiveProtocol { get; private set; } = StorageProtocol.Sata;
    public string? SataId { get; private set; }
    public string? NvmeId { get; private set; }
    public IReadOnlyList<StorageDevice> Devices { get; private set; } = [];
    public long BeginDiscovery() => revision;

    public void SelectTab(StorageProtocol protocol) { ActiveProtocol = protocol; userChoseTab = true; revision++; }
    public void SelectDevice(StorageProtocol protocol, string? id)
    {
        if (protocol == StorageProtocol.Sata) SataId = id;
        if (protocol == StorageProtocol.Nvme) NvmeId = id;
        revision++;
    }

    public void Apply(IReadOnlyList<StorageDevice> devices, long startedAtRevision)
    {
        var initial = Devices.Count == 0 && SataId is null && NvmeId is null;
        Devices = devices.OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
        var system = Devices.Where(d => d.IsUnambiguousSystemDisk).ToArray();
        var preferred = system.Length == 1 && system[0].Protocol != StorageProtocol.Unknown ? system[0] : null;
        string? Resolve(StorageProtocol protocol, string? current) =>
            Devices.FirstOrDefault(d => d.Protocol == protocol && d.Id == current)?.Id ??
            (initial && preferred?.Protocol == protocol ? preferred.Id : null) ??
            Devices.FirstOrDefault(d => d.Protocol == protocol)?.Id;
        SataId = Resolve(StorageProtocol.Sata, SataId);
        NvmeId = Resolve(StorageProtocol.Nvme, NvmeId);
        if (revision != startedAtRevision || userChoseTab) return;
        if (initial) ActiveProtocol = preferred?.Protocol ?? (SataId is not null ? StorageProtocol.Sata :
            NvmeId is not null ? StorageProtocol.Nvme : StorageProtocol.Sata);
        else if (ActiveProtocol == StorageProtocol.Sata && SataId is null && NvmeId is not null) ActiveProtocol = StorageProtocol.Nvme;
        else if (ActiveProtocol == StorageProtocol.Nvme && NvmeId is null && SataId is not null) ActiveProtocol = StorageProtocol.Sata;
    }
}
