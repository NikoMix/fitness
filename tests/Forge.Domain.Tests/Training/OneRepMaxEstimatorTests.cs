using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

/// <summary>
/// Tests for <see cref="OneRepMaxEstimator"/>.
/// </summary>
/// <remarks>
/// Users make loading decisions from these numbers, so the important behaviour is not only
/// that the arithmetic matches the published formulae, but that the estimator declines to
/// answer when the input cannot support a meaningful estimate.
/// </remarks>
public sealed class OneRepMaxEstimatorTests
{
    [Fact]
    public void A_single_repetition_is_returned_unchanged()
    {
        // One rep already is the maximum. Any formula that adjusts it is wrong by definition.
        var result = OneRepMaxEstimator.Estimate(Mass.FromKilograms(100m), 1);

        result.ShouldNotBeNull();
        result.Value.Kilograms.ShouldBe(100m);
    }

    [Fact]
    public void Epley_matches_the_published_formula()
    {
        // Epley: 1RM = w * (1 + r/30) => 100 * (1 + 5/30) = 116.67
        var result = OneRepMaxEstimator.Estimate(Mass.FromKilograms(100m), 5, OneRepMaxFormula.Epley);

        result!.Value.Kilograms.ShouldBe(116.67m, tolerance: 0.01m);
    }

    [Fact]
    public void Brzycki_matches_the_published_formula()
    {
        // Brzycki: 1RM = w * 36 / (37 - r) => 100 * 36 / 32 = 112.50
        var result = OneRepMaxEstimator.Estimate(Mass.FromKilograms(100m), 5, OneRepMaxFormula.Brzycki);

        result!.Value.Kilograms.ShouldBe(112.50m, tolerance: 0.01m);
    }

    [Fact]
    public void Brzycki_reads_lower_than_Epley_below_ten_repetitions()
    {
        // The two fits diverge below ten reps and coincide exactly at ten, where both reduce
        // to 4/3 of the load. Showing which formula produced a number is what makes that
        // behaviour explicable rather than looking like a defect.
        var epley = OneRepMaxEstimator.Estimate(Mass.FromKilograms(100m), 5, OneRepMaxFormula.Epley);
        var brzycki = OneRepMaxEstimator.Estimate(Mass.FromKilograms(100m), 5, OneRepMaxFormula.Brzycki);

        brzycki!.Value.ShouldBeLessThan(epley!.Value);
    }

    [Fact]
    public void The_two_formulae_agree_exactly_at_ten_repetitions()
    {
        // Epley:   100 * (1 + 10/30) = 133.33
        // Brzycki: 100 * 36 / (37-10) = 100 * 36/27 = 133.33
        // This coincidence is a genuine property of the two published fits, not a rounding
        // artefact, and it is the reason ten is a natural ceiling for the supported range.
        var epley = OneRepMaxEstimator.Estimate(Mass.FromKilograms(100m), 10, OneRepMaxFormula.Epley);
        var brzycki = OneRepMaxEstimator.Estimate(Mass.FromKilograms(100m), 10, OneRepMaxFormula.Brzycki);

        brzycki!.Value.Kilograms.ShouldBe(epley!.Value.Kilograms, tolerance: 0.01m);
        epley.Value.Kilograms.ShouldBe(133.33m, tolerance: 0.01m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(50)]
    public void Repetitions_outside_the_supported_range_produce_no_estimate(int repetitions)
    {
        // Returning null rather than a number is deliberate. Above ten reps the estimate
        // reflects endurance more than maximal strength, and a confident figure would mislead
        // someone choosing their next working weight.
        OneRepMaxEstimator.Estimate(Mass.FromKilograms(100m), repetitions).ShouldBeNull();
    }

    [Fact]
    public void A_bodyweight_set_with_no_load_produces_no_estimate()
    {
        OneRepMaxEstimator.Estimate(Mass.Zero, 5).ShouldBeNull();
    }

    [Fact]
    public void The_estimate_never_falls_below_the_load_actually_lifted()
    {
        // An invariant that must hold for every valid input: you can certainly lift at least
        // what you just lifted. A violation signals a formula or rounding regression.
        var load = Mass.FromKilograms(80m);

        for (var reps = 1; reps <= OneRepMaxEstimator.MaximumSupportedRepetitions; reps++)
        {
            foreach (var formula in Enum.GetValues<OneRepMaxFormula>())
            {
                var estimate = OneRepMaxEstimator.Estimate(load, reps, formula);

                estimate.ShouldNotBeNull();
                estimate.Value.ShouldBeGreaterThanOrEqualTo(load);
            }
        }
    }
}
