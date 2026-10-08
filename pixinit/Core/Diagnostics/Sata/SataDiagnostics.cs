using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Assessment;

namespace pixinit.Core.Diagnostics.Sata;

public sealed record SataSmartAttribute(byte Id, string Name, int? Current, int? Worst,
    int? Threshold, string RawValue, string Status, string Detail)
{
    public ushort Flags { get; init; }
    public byte[] RawBytes { get; init; } = [];
    public int Slot { get; init; }
    public string InterpretationSource { get; init; } = "Unknown vendor interpretation";
}

public sealed record SataDiagnostics(string DeviceId, Metric<bool> SmartPassed,
    Metric<double> Temperature, Metric<double> Wear, IReadOnlyList<SataSmartAttribute> Attributes,
    string? EnduranceDetails, string? AtaDetails, string? InterfaceDetails)
{
    public TbwInfo? Tbw { get; init; }
    public ThermalInfo? Thermal { get; init; }
    public double? PowerOnHours { get; init; }
    public HealthAssessment? Assessment { get; init; }
    public AtaIdentity? Identity { get; init; }
    public IReadOnlyList<AtaCommandResult> Commands { get; init; } = [];
    public string StatusExplanation { get; init; } = "Not queried";
    public string Summary { get; init; } = "Not queried";
    public string Discrepancies { get; init; } = "";
}
