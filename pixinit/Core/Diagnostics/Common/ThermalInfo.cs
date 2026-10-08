namespace pixinit.Core.Diagnostics.Common;

public enum ThermalState { Unknown, Green, Yellow, Red }
public sealed record ThermalInfo(double? IdleC, double? LoadC, ThermalState State,
    double? ObservedC = null, string Source = "Unavailable", DateTimeOffset? ObservedAt = null, bool InterpretationVerified = true)
{
    public DateTimeOffset? IdleTempObservedAt { get; init; }
    public DateTimeOffset? LoadTempObservedAt { get; init; }
    public string IdleCondition { get; init; } = "Not captured";
    public string LoadCondition { get; init; } = "Not captured";
    public double? CurrentTemp => ObservedC;
    public double? IdleTemp => IdleC;
    public double? LoadTemp => LoadC;
    public static ThermalState Classify(double? celsius) => celsius is not double value || !double.IsFinite(value)
        ? ThermalState.Unknown : value > 80 ? ThermalState.Red : value >= 75 ? ThermalState.Yellow : ThermalState.Green;
    public static ThermalInfo FromMetric(Metric<double> metric, bool interpretationVerified = true) => metric.Availability == Availability.Available
        ? new(null, null, Classify(metric.Value), metric.Value, metric.Source ?? "Unavailable", metric.ObservedAt, interpretationVerified)
        : new(null, null, ThermalState.Unknown);
}
