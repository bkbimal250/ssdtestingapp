namespace pixinit.Core.Diagnostics.Common;

public sealed record TbwInfo(double? WrittenTB, double RatedTB = 600, bool InterpretationVerified = true,
    string Source = "Unavailable", bool RatingVerified = false)
{
    public double? RemainingPercent => WrittenTB is double written && double.IsFinite(written) && written >= 0 && RatedTB > 0 && double.IsFinite(RatedTB)
        ? Math.Clamp(100 * (RatedTB - written) / RatedTB, 0, 100) : null;
}
