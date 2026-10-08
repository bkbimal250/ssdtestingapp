using System.IO;
using pixinit.Core.Devices;
using pixinit.Core.Diagnostics.Common;

namespace pixinit.ViewModels.Shared;

public abstract partial class DeviceViewModel : ObservableObject
{
    public StorageDevice? Device { get; private set; }
    public string Model => Device?.DisplayName ?? "No drive selected";
    public string Serial => string.IsNullOrWhiteSpace(Device?.Serial) ? "Unavailable" : Device.Serial;
    public string ProtocolDisplay => Device?.Protocol switch { StorageProtocol.Nvme => "NVMe", StorageProtocol.Sata => "SATA", _ => "Not established" };
    public string MediaDisplay => Device?.Media switch { StorageMedia.Ssd => "SSD", StorageMedia.Hdd => "HDD", _ => "Not confirmed" };
    public string AccessDisplay => Device?.Access switch { DeviceAccess.Available => "Available", DeviceAccess.AccessDenied => "Access denied", DeviceAccess.Limited => "Limited", _ => "Unavailable" };
    public string MediaEvidence => Device is null ? "Select a drive to view media evidence." :
        (Device.IncursSeekPenalty switch { true => "Windows reports seek penalty.", false => "Windows reports no seek penalty.", _ => "Seek-penalty evidence is unavailable." }) +
        (Device.Media == StorageMedia.Unknown ? " SSD/HDD type is not confirmed by discovery. This is not a health warning; NVMe/SATA describes the protocol." : " Media type is shown separately from protocol.");
    public string Connection => $"{ProtocolDisplay} · Media: {MediaDisplay} · Reported bus: {Device?.Bus.ToString() ?? "Unavailable"} · Metadata access: {AccessDisplay}";
    public string DiscoveryDetails => Device is null ? "No discovery information available." :
        $"Physical-disk capacity: {(Device.CapacityBytes is long n ? $"{n:N0} bytes ({Device.CapacityDisplay}, decimal)" : "Unavailable")}\n" +
        $"Media: {MediaDisplay} · {MediaEvidence}\n" +
        $"Firmware: {Device.Firmware ?? "Unavailable"} · Removable: {(Device.Removable is bool r ? r ? "Yes" : "No" : "Unknown")}\n" +
        $"Volumes / mount points: {(Device.MountPoints.Count == 0 ? "Unavailable or none mapped" : string.Join(", ", Device.MountPoints))}\n" +
        $"Windows physical disk: {(Device.IsUnambiguousSystemDisk ? "Unambiguously mapped" : "Not established for this disk")}\n" +
        $"Source: {Device.Source} · Observed: {Device.ObservedAt:g}\n{Device.Limitations}\n" +
        $"Identity: {Device.IdentityEvidence}\nInterface: {Device.InterfacePath ?? "Unavailable"}\nPnP instance: {Device.InstanceId ?? "Unavailable"}\nSession key: {Device.Id}";
    private bool stale;
    public void SetStale(bool value) { stale = value; Changed(nameof(DataStatus)); }
    public string DeviceId => Device?.Id ?? "Unavailable";
    private bool hasResult;
    public string DataStatus => stale ? "Stale discovery information · Rescan to refresh" : Device is null ? "No drive selected · No live readings" : hasResult ? "Provider result received · Availability shown per reading" : "Diagnostics not yet queried · No health readings";

    public void SetDevice(StorageDevice? device)
    {
        if (Device?.Id != device?.Id) ResetThermalCapture();
        Device = device;
        stale = false;
        hasResult = false;
        ClearResults();
        SetVolumes();
        Changed(nameof(ProtocolDisplay)); Changed(nameof(MediaDisplay)); Changed(nameof(AccessDisplay)); Changed(nameof(MediaEvidence));
        Changed(nameof(Model)); Changed(nameof(Serial)); Changed(nameof(Connection)); Changed(nameof(DeviceId)); Changed(nameof(DataStatus)); Changed(nameof(DiscoveryDetails)); Changed(nameof(EntireSsdInfo));
    }

    protected abstract void ClearResults();
    protected void ResultReceived() { NotifyThermalCapture(); hasResult = true; Changed(nameof(DataStatus)); }
    protected static string Format<T>(Metric<T>? metric) where T : struct => metric is null ? "Unavailable" :
        metric.Availability == Availability.Available ? $"{metric.Value} {metric.Unit}".Trim() :
        metric.Availability == Availability.AccessDenied ? "Access denied" : metric.Availability.ToString();
}
