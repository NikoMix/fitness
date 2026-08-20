using Forge.Domain.Measurement;

namespace Forge.Domain.Training;

/// <summary>
/// Estimates a one-repetition maximum from a submaximal set.
/// </summary>
/// <remarks>
/// <para>
/// Every formula of this kind is an approximation fitted to population data, and none is
/// accurate for an individual. Error grows sharply with repetition count: above roughly ten
/// reps the estimate says more about muscular endurance than about maximal strength.
/// </para>
/// <para>
/// Forge therefore treats an estimated 1RM as a trend line rather than a number to train
/// against, always shows which formula produced it, and refuses to estimate where the input
/// makes the result meaningless. Presenting a confident single figure derived from a set of
/// twenty reps would be misleading, and users make loading decisions from these numbers.
/// </para>
/// </remarks>
public static class OneRepMaxEstimator
{
    /// <summary>
    /// Highest repetition count for which an estimate is offered.
    /// </summary>
    /// <remarks>
    /// Ten is the conventional ceiling for the Epley and Brzycki fits. Beyond it the divergence
    /// between formulae exceeds any useful precision.
    /// </remarks>
    public const int MaximumSupportedRepetitions = 10;

    /// <summary>
    /// Estimates a one-repetition maximum, or returns <see langword="null"/> when the input
    /// cannot support a meaningful estimate.
    /// </summary>
    /// <param name="load">Load lifted. Must be greater than zero.</param>
    /// <param name="repetitions">Repetitions completed, from 1 to <see cref="MaximumSupportedRepetitions"/>.</param>
    /// <param name="formula">Which published fit to apply.</param>
    /// <returns>The estimate, or <see langword="null"/> if the input is out of range.</returns>
    public static Mass? Estimate(Mass load, int repetitions, OneRepMaxFormula formula = OneRepMaxFormula.Epley)
    {
        if (load <= Mass.Zero || repetitions < 1 || repetitions > MaximumSupportedRepetitions)
        {
            return null;
        }

        // A single repetition is already the maximum; no formula should adjust it.
        if (repetitions == 1)
        {
            return load;
        }

        var w = load.Kilograms;
        var r = repetitions;

        var estimate = formula switch
        {
            // Epley (1985): 1RM = w * (1 + r / 30)
            OneRepMaxFormula.Epley => w * (1m + r / 30m),

            // Brzycki (1993): 1RM = w * 36 / (37 - r)
            // Undefined at r = 37; the ten-rep ceiling keeps us far from that singularity.
            OneRepMaxFormula.Brzycki => w * 36m / (37m - r),

            _ => throw new ArgumentOutOfRangeException(nameof(formula), formula, "Unknown formula.")
        };

        return Mass.FromKilograms(decimal.Round(estimate, 2));
    }
}

/// <summary>Published one-repetition-maximum estimation formulae.</summary>
/// <remarks>
/// The two fits coincide exactly at ten repetitions, where both reduce to four thirds of the
/// load. Below ten Brzycki reads lower than Epley, and above ten it reads higher. That
/// crossover is a real property of the published equations, which is part of why ten is a
/// natural ceiling for a supported range.
/// </remarks>
public enum OneRepMaxFormula
{
    /// <summary>Epley (1985). Reads higher than Brzycki below ten repetitions.</summary>
    Epley = 0,

    /// <summary>Brzycki (1993). Reads lower than Epley below ten repetitions.</summary>
    Brzycki = 1
}
