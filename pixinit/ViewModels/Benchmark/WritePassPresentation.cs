using pixinit.Core.Benchmark;

namespace pixinit.ViewModels.Benchmark;

internal sealed record WritePassPresentation(double? FilteredAverage, int Retained, int Total, string Tooltip)
{
    internal static bool IsLowOutlier(BenchmarkPassResult pass, IReadOnlyList<BenchmarkPassResult> group) =>
        pass.IncludeInAverage && pass.IsValidForDeviceAttribution && pass.Measurement.State == BenchmarkOperationState.Completed &&
        pass.Measurement.Operation is BenchmarkOperation.SequentialWrite or BenchmarkOperation.RandomWrite &&
        (pass.TotalBytes < 536870912 && pass.Measurement.MegabytesPerSecond < 100) &&
        group.Any(other => other.Profile == pass.Profile && other.Measurement.Operation == pass.Measurement.Operation &&
            other.IncludeInAverage && other.IsValidForDeviceAttribution && other.Measurement.State == BenchmarkOperationState.Completed && other.Measurement.MegabytesPerSecond > 500);

    internal static WritePassPresentation Create(IReadOnlyList<BenchmarkPassResult> group)
    {
        var measured = group.Where(p => p.IncludeInAverage && p.Measurement.State == BenchmarkOperationState.Completed).ToArray();
        var retained = measured.Where(p => !IsLowOutlier(p, group)).ToArray();
        bool filtered = retained.Length < measured.Length;
        string tooltip = string.Join("\n", group.Select(p => p.Display + (IsLowOutlier(p, group) ? "; low-write outlier excluded from filtered average only; cache exhaustion not confirmed" : "")));
        return new(filtered && retained.Length > 0 ? retained.Average(p => p.Measurement.MegabytesPerSecond) : null, retained.Length, measured.Length, tooltip);
    }
}
