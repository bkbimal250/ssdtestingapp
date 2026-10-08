using System.IO;
using pixinit.Core.Devices;

namespace pixinit.ViewModels.Shared;

public abstract partial class DeviceViewModel
{
    public sealed record InfoRow(string Label, string Value, bool NeedsDiagnostics = false);
    private long storageGeneration;
    private string selectedVolume = "";
    public IReadOnlyList<string> Volumes { get; private set; } = [];
    public string SelectedVolume { get => selectedVolume; set { if (selectedVolume == value || !Volumes.Contains(value, StringComparer.OrdinalIgnoreCase)) return; selectedVolume = value; Changed(); _ = RefreshStorageAsync(); } }
    public long? TotalStorageBytes { get; private set; }
    public long? UsedStorageBytes { get; private set; }
    public long? FreeStorageBytes { get; private set; }
    public double? UsedPercent => TotalStorageBytes is > 0 && UsedStorageBytes is long used ? used * 100d / TotalStorageBytes.Value : null;
    public bool HasStorageUsage => UsedPercent is not null;
    public string StorageStatusText => StorageMessage;
    public string StorageMessage { get; private set; } = "No drive selected";
    public string TotalStorageDisplay => BytesDisplay(TotalStorageBytes);
    public string UsedStorageDisplay => BytesDisplay(UsedStorageBytes);
    public string FreeStorageDisplay => BytesDisplay(FreeStorageBytes);
    public string UsedPercentDisplay => UsedPercent is double p ? $"{p:F1}% used" : "Usage unavailable";
    internal Func<string, (long Total, long Free)> ReadVolumeCapacity { get; set; } = volume =>
    {
        var drive = new DriveInfo(volume);
        if (!drive.IsReady) throw new IOException("Volume is not ready");
        return (drive.TotalSize, drive.AvailableFreeSpace);
    };
    public static string BytesDisplay(long? bytes) => bytes is long n ? $"{n / 1073741824d:N2} GiB / {n / 1e9:N2} GB" : "N/A";
    private void SetVolumes()
    {
        Volumes = Device?.MountPoints.Where(IsDriveRoot).Select(p => char.ToUpperInvariant(p[0]) + @":\").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToArray() ?? [];
        selectedVolume = Volumes.FirstOrDefault() ?? "";
        Changed(nameof(Volumes)); Changed(nameof(SelectedVolume));
        _ = RefreshStorageAsync();
    }
    private static bool IsDriveRoot(string path) => path.Length is 2 or 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path.Length == 2 || path[2] == '\\');
    public async Task RefreshStorageAsync()
    {
        long current = ++storageGeneration;
        string volume = selectedVolume;
        TotalStorageBytes = Device?.CapacityBytes; UsedStorageBytes = FreeStorageBytes = null;
        StorageMessage = Device is null ? "No drive selected" : volume.Length == 0 ? Device.MountPoints.Any(p => Path.IsPathRooted(p)) ? "Mapped volume has no drive letter - showing device capacity only" : "No mounted volume - showing device capacity only" : "Reading mounted volume usage...";
        NotifyStorage();
        if (volume.Length == 0) return;
        try
        {
            var reader = ReadVolumeCapacity;
            var (total, free) = await Task.Run(() => reader(volume));
            if (current != storageGeneration || volume != selectedVolume) return;
            if (total <= 0 || free < 0 || free > total) throw new IOException("Volume capacity is inconsistent");
            TotalStorageBytes = total; FreeStorageBytes = free; UsedStorageBytes = total - free;
            StorageMessage = "Selected filesystem volume usage, not physical disk capacity. Free means space available to this user; quotas can affect it. Spanned volumes are not attributed to one disk.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            if (current != storageGeneration) return;
            TotalStorageBytes = Device?.CapacityBytes; UsedStorageBytes = FreeStorageBytes = null;
            StorageMessage = "Volume usage unavailable - showing device capacity only. " + ex.Message;
        }
        NotifyStorage();
    }
    private void NotifyStorage()
    {
        foreach (var name in new[] { nameof(TotalStorageBytes), nameof(UsedStorageBytes), nameof(FreeStorageBytes), nameof(UsedPercent), nameof(HasStorageUsage), nameof(StorageMessage), nameof(StorageStatusText), nameof(TotalStorageDisplay), nameof(UsedStorageDisplay), nameof(FreeStorageDisplay), nameof(UsedPercentDisplay) }) Changed(name);
    }
    public virtual IReadOnlyList<InfoRow> EntireSsdInfo => IdentityRows().Concat(Enumerable.Range(0, 8).Select(_ => new InfoRow("", "N/A"))).ToArray();
    protected IEnumerable<InfoRow> IdentityRows(string? model = null, string? firmware = null, string? serial = null) => new InfoRow[]
    {
        new("Model", model ?? Device?.Model ?? "N/A"), new("Physical capacity", BytesDisplay(Device?.CapacityBytes)),
        new("Firmware", firmware ?? Device?.Firmware ?? "N/A"), new("Full serial", serial ?? Serial), new("Protocol", ProtocolDisplay), new("Reported bus", Device?.Bus.ToString() ?? "N/A"), new("Media", MediaDisplay), new("Access", AccessDisplay)
    };
    protected static string FirstPageValue(string value) => value is "Unavailable" or "Not queried" ? "N/A" : value;
}
