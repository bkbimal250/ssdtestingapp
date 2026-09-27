using pixinit.Core.Diagnostics.Common;
using pixinit.Core.Assessment;

namespace pixinit.Core.Diagnostics.Nvme;

public sealed record NvmeDiagnostics(string DeviceId, Metric<byte> CriticalWarning,
    Metric<double> Temperature, Metric<double> AvailableSpare, Metric<double> PercentageUsed,
    string? UsageCounters, string? ThermalDetails, string? ControllerDetails, string? ErrorDetails)
{
    public HealthAssessment? Assessment { get; init; }
    public NvmeController? Controller { get; init; }
    public NvmeHealth? Health { get; init; }
    public IReadOnlyList<NvmeError>? ErrorEntries { get; init; }
    public IReadOnlyList<NvmeResponse> Responses { get; init; } = [];
    public string WarningDetails { get; init; } = "Not queried";
    public string Summary { get; init; } = "Not queried";
    public string SpareThreshold { get; init; } = "Unavailable";
}
