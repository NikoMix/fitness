using Forge.Domain.Measurement;

namespace Forge.Domain.Analytics;

public sealed record TrainingLoadPoint(DateOnly Date, Mass Volume);

public sealed record TrainingLoadRatio(DateOnly AsOf, Mass AcuteLoad, Mass ChronicLoad, decimal Ratio, string Caveat);

/// <summary>
/// Calculates a simple acute:chronic workload-style ratio from local training volume.
/// </summary>
/// <remarks>
/// This is only a rough descriptive signal. The evidence that acute:chronic workload ratios
/// reliably predict injury for an individual lifter is weak and contested, so Forge must present
/// the result as context for reflection, not as a warning, diagnosis, or prescription.
/// </remarks>
public sealed class TrainingLoadCalculator
{
    public const int DefaultAcuteDays = 7;
    public const int DefaultChronicDays = 28;
    public const string EvidenceCaveat = "Acute:chronic workload ratios are a rough context signal with weak, contested evidence for individual injury prediction.";

    public static TrainingLoadRatio? Calculate(IEnumerable<TrainingLoadPoint> points, DateOnly asOf, int acuteDays = DefaultAcuteDays, int chronicDays = DefaultChronicDays)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfLessThan(acuteDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chronicDays, acuteDays);

        var materialized = points.ToList();
        var acuteStart = asOf.AddDays(-acuteDays + 1);
        var chronicStart = asOf.AddDays(-chronicDays + 1);

        var acute = SumBetween(materialized, acuteStart, asOf);
        var chronic = SumBetween(materialized, chronicStart, asOf);

        if (chronic == Mass.Zero)
        {
            return null;
        }

        var acuteDaily = acute.Kilograms / acuteDays;
        var chronicDaily = chronic.Kilograms / chronicDays;
        if (chronicDaily == 0m)
        {
            return null;
        }

        return new TrainingLoadRatio(asOf, acute, chronic, decimal.Round(acuteDaily / chronicDaily, 2), EvidenceCaveat);
    }

    private static Mass SumBetween(IEnumerable<TrainingLoadPoint> points, DateOnly start, DateOnly end)
        => points
            .Where(point => point.Date >= start && point.Date <= end)
            .Aggregate(Mass.Zero, (sum, point) => sum + point.Volume);
}
