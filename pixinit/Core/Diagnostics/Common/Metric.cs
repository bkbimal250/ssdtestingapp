namespace pixinit.Core.Diagnostics.Common;

public enum Availability { Available, Unavailable, Unsupported, AccessDenied, Error }
public enum ScanState { NotStarted, Running, Completed, Cancelled, Failed }

// An available zero is different from missing data. Interpretation is supplied by
// the protocol provider, not inferred by the presentation layer.
public sealed record Metric<T> where T : struct
{
    public T? Value { get; }
    public string Unit { get; }
    public Availability Availability { get; }
    public string? Source { get; }
    public DateTimeOffset? ObservedAt { get; }
    public string? Interpretation { get; }

    public Metric(T? value, string unit, Availability availability, string? source = null,
        DateTimeOffset? observedAt = null, string? interpretation = null)
    {
        if (availability == Availability.Available && (value is null || string.IsNullOrWhiteSpace(source) || observedAt is null))
            throw new ArgumentException("Available readings require a value, source and observation time.");
        if (availability != Availability.Available && value is not null)
            throw new ArgumentException("Missing readings cannot carry live values.");
        Value = value; Unit = unit; Availability = availability;
        Source = source; ObservedAt = observedAt; Interpretation = interpretation;
    }

    public static Metric<T> Missing(string unit = "") => new(null, unit, Availability.Unavailable);
}
