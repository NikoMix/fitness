namespace Forge.Core.Abstractions.Health;

/// <summary>
/// What a window of platform health samples adds up to.
/// </summary>
/// <remarks>
/// Every value is nullable on purpose. Zero and "we received nothing" are different facts here: on
/// HealthKit an empty result may mean the user refused read access, so rendering a confident 0 kcal
/// would be presenting a permission failure as a measurement.
/// </remarks>
/// <param name="Steps">Total steps, or <see langword="null"/> when no step samples arrived.</param>
/// <param name="Sleep">Total time asleep, or <see langword="null"/> when no sleep samples arrived.</param>
/// <param name="WaterLitres">Total fluid intake in litres, or <see langword="null"/>.</param>
/// <param name="ActiveEnergyKilocalories">Total active energy burned, or <see langword="null"/>.</param>
/// <param name="AverageHeartRate">Mean beats per minute across the samples, or <see langword="null"/>.</param>
/// <param name="BodyMassKilograms">Most recent body mass reading, or <see langword="null"/>.</param>
public sealed record HealthSampleTotals(
    long? Steps,
    TimeSpan? Sleep,
    double? WaterLitres,
    double? ActiveEnergyKilocalories,
    double? AverageHeartRate,
    double? BodyMassKilograms)
{
    /// <summary>Totals for a window in which nothing at all was returned.</summary>
    public static HealthSampleTotals Empty { get; } = new(null, null, null, null, null, null);

    /// <summary>Whether any category produced a value.</summary>
    public bool HasAnyValue =>
        Steps is not null ||
        Sleep is not null ||
        WaterLitres is not null ||
        ActiveEnergyKilocalories is not null ||
        AverageHeartRate is not null ||
        BodyMassKilograms is not null;
}

/// <summary>
/// Reduces platform health samples to the values Forge's screens actually display.
/// </summary>
/// <remarks>
/// This lives in <c>Forge.Core</c> rather than in either platform implementation so the arithmetic
/// is written and tested once. Both stores hand back the same shapes - a list of typed samples -
/// and the interesting mistakes (summing an instantaneous heart rate, taking the first weigh-in
/// rather than the last) are identical on both.
/// </remarks>
public static class HealthSampleAggregator
{
    /// <summary>Reduces a set of samples to per-category totals.</summary>
    /// <param name="samples">Samples returned by a platform read. May be empty.</param>
    /// <returns>The totals; categories with no samples stay <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> is null.</exception>
    public static HealthSampleTotals Summarise(IReadOnlyList<HealthSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        long? steps = null;
        TimeSpan? sleep = null;
        double? water = null;
        double? activeEnergy = null;
        double heartRateSum = 0;
        var heartRateCount = 0;
        double? bodyMass = null;
        DateTimeOffset? bodyMassAt = null;

        foreach (var sample in samples)
        {
            switch (sample)
            {
                case StepsHealthSample step:
                    steps = (steps ?? 0) + step.Count;
                    break;

                case SleepHealthSample asleep:
                    sleep = (sleep ?? TimeSpan.Zero) + asleep.Duration;
                    break;

                case WaterHealthSample drink:
                    water = (water ?? 0d) + drink.Litres;
                    break;

                case ActiveEnergyHealthSample energy:
                    activeEnergy = (activeEnergy ?? 0d) + energy.Kilocalories;
                    break;

                // A heart rate is an instantaneous reading, so the only meaningful reduction is a
                // mean. Summing would produce a number in the thousands and look like a bug report.
                case HeartRateHealthSample heartRate:
                    heartRateSum += heartRate.BeatsPerMinute;
                    heartRateCount++;
                    break;

                // Weight is a point-in-time fact, so the newest sample wins rather than the last one
                // the platform happened to hand back; read order is not guaranteed to be sorted.
                case BodyMassHealthSample mass when bodyMassAt is null || mass.End >= bodyMassAt:
                    bodyMass = mass.Kilograms;
                    bodyMassAt = mass.End;
                    break;

                default:
                    break;
            }
        }

        return new HealthSampleTotals(
            steps,
            sleep,
            water,
            activeEnergy,
            heartRateCount is 0 ? null : heartRateSum / heartRateCount,
            bodyMass);
    }
}
