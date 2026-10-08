namespace pixinit.ViewModels.Shared;
public sealed class OtherDeviceViewModel : DeviceViewModel
{
    public string Assessment => "Diagnostics require a confirmed SATA or NVMe protocol";
    public string Coverage => "Discovery and mounted volume usage only; USB/RAID passthrough support is not established";
    public string ObservedLocal => Device?.ObservedAt?.ToLocalTime().ToString("g") ?? "Not observed";
    public string FirstPageWarnings => Device?.Limitations ?? "Select an unidentified device";
    public override IReadOnlyList<InfoRow> EntireSsdInfo => IdentityRows().Concat(new[] { "Power-on hours", "Power cycles", "Host writes", "Host reads", "Available spare", "Percentage Used (consumed)", "Critical warning", "Observed temperature" }.Select(label => new InfoRow(label, "N/A - confirmed protocol/passthrough required"))).ToArray();
    protected override void ClearResults() { }
}
