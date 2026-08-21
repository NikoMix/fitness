using Forge.Core.Abstractions.Health;
using Shouldly;

namespace Forge.Core.Tests.Health;

/// <summary>
/// Covers the reduction from platform samples to displayed values. The failure modes here are
/// quiet ones: a summed heart rate looks like a bug report, and a nullable that becomes zero turns
/// a permission refusal into a confident measurement of nothing.
/// </summary>
public sealed class HealthSampleAggregatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 21, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Nothing_read_produces_nulls_rather_than_zeroes()
    {
        // The distinction the whole feature turns on. Zero steps is a measurement; no steps sample
        // may be a refused permission, and the two must not render the same way.
        var totals = HealthSampleAggregator.Summarise([]);

        totals.Steps.ShouldBeNull();
        totals.Sleep.ShouldBeNull();
        totals.WaterLitres.ShouldBeNull();
        totals.ActiveEnergyKilocalories.ShouldBeNull();
        totals.AverageHeartRate.ShouldBeNull();
        totals.BodyMassKilograms.ShouldBeNull();
        totals.HasAnyValue.ShouldBeFalse();
    }

    [Fact]
    public void A_genuine_zero_is_distinguishable_from_no_data()
    {
        var totals = HealthSampleAggregator.Summarise(
            [new StepsHealthSample(Start, Start.AddHours(1), 0)]);

        totals.Steps.ShouldBe(0);
        totals.HasAnyValue.ShouldBeTrue();
    }

    [Fact]
    public void Cumulative_categories_are_summed()
    {
        var totals = HealthSampleAggregator.Summarise(
        [
            new StepsHealthSample(Start, Start.AddHours(1), 1200),
            new StepsHealthSample(Start.AddHours(1), Start.AddHours(2), 800),
            new WaterHealthSample(Start, Start.AddMinutes(1), 0.25),
            new WaterHealthSample(Start.AddHours(2), Start.AddHours(2).AddMinutes(1), 0.5),
            new ActiveEnergyHealthSample(Start, Start.AddHours(1), 120),
            new ActiveEnergyHealthSample(Start.AddHours(1), Start.AddHours(2), 80)
        ]);

        totals.Steps.ShouldBe(2000);
        totals.WaterLitres!.Value.ShouldBe(0.75, 0.000001);
        totals.ActiveEnergyKilocalories!.Value.ShouldBe(200, 0.000001);
    }

    [Fact]
    public void Sleep_durations_are_summed_across_fragmented_sessions()
    {
        var totals = HealthSampleAggregator.Summarise(
        [
            new SleepHealthSample(Start, Start.AddHours(3), TimeSpan.FromHours(3)),
            new SleepHealthSample(Start.AddHours(4), Start.AddHours(8), TimeSpan.FromHours(3.5))
        ]);

        totals.Sleep.ShouldBe(TimeSpan.FromHours(6.5));
    }

    [Fact]
    public void Heart_rate_is_averaged_not_summed()
    {
        var totals = HealthSampleAggregator.Summarise(
        [
            new HeartRateHealthSample(Start, Start, 60),
            new HeartRateHealthSample(Start.AddMinutes(1), Start.AddMinutes(1), 80),
            new HeartRateHealthSample(Start.AddMinutes(2), Start.AddMinutes(2), 100)
        ]);

        totals.AverageHeartRate!.Value.ShouldBe(80, 0.000001);
    }

    [Fact]
    public void Body_mass_takes_the_newest_reading_regardless_of_arrival_order()
    {
        // Read order is not guaranteed to be sorted, and weight is a point-in-time fact rather
        // than a total - taking "the last one handed back" would show a stale weigh-in.
        var totals = HealthSampleAggregator.Summarise(
        [
            new BodyMassHealthSample(Start.AddDays(2), Start.AddDays(2), 81.2),
            new BodyMassHealthSample(Start, Start, 83.4),
            new BodyMassHealthSample(Start.AddDays(1), Start.AddDays(1), 82.0)
        ]);

        totals.BodyMassKilograms!.Value.ShouldBe(81.2, 0.000001);
    }

    [Fact]
    public void A_single_weigh_in_is_used_even_when_it_is_the_only_sample()
    {
        var totals = HealthSampleAggregator.Summarise(
            [new BodyMassHealthSample(Start, Start, 79.5)]);

        totals.BodyMassKilograms!.Value.ShouldBe(79.5, 0.000001);
    }

    [Fact]
    public void Categories_are_reduced_independently()
    {
        var totals = HealthSampleAggregator.Summarise(
        [
            new StepsHealthSample(Start, Start.AddHours(1), 5000),
            new HeartRateHealthSample(Start, Start, 70)
        ]);

        totals.Steps.ShouldBe(5000);
        totals.AverageHeartRate!.Value.ShouldBe(70, 0.000001);
        totals.WaterLitres.ShouldBeNull();
        totals.Sleep.ShouldBeNull();
    }

    [Fact]
    public void Unhandled_sample_types_are_ignored_rather_than_throwing()
    {
        // A future platform category must degrade to "not shown", never to a crash on a screen the
        // user opened to check their sleep.
        var totals = HealthSampleAggregator.Summarise(
        [
            new DietaryEnergyHealthSample(Start, Start.AddHours(1), 400),
            new StepsHealthSample(Start, Start.AddHours(1), 100)
        ]);

        totals.Steps.ShouldBe(100);
        totals.HasAnyValue.ShouldBeTrue();
    }

    [Fact]
    public void Summarise_rejects_null()
    {
        Should.Throw<ArgumentNullException>(() => HealthSampleAggregator.Summarise(null!));
    }

    [Fact]
    public void Empty_totals_report_no_values()
    {
        HealthSampleTotals.Empty.HasAnyValue.ShouldBeFalse();
    }
}
