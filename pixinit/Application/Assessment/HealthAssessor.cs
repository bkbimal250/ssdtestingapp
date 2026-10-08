using pixinit.Core.Diagnostics.Common;
namespace pixinit.Application.Assessment;

public static class HealthAssessor
{
    public static ThermalState TemperatureState(double? celsius) => ThermalInfo.Classify(celsius);
    public static ThermalInfo ObservedTemperature(Metric<double> metric) => ThermalInfo.FromMetric(metric);
    public static string EnduranceState(TbwInfo? info) => info?.RemainingPercent is not double percent ? "Unavailable"
        : !info.InterpretationVerified ? "Unverified SMART interpretation"
        : percent < 10 ? info.RatingVerified ? "Warning" : "Warning against 600 TB reference only" : info.RatingVerified ? "Within rated allowance" : "600 TB reference only; manufacturer rating unverified";
}
