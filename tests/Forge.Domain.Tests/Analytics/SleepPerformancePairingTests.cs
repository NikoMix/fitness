using System.Globalization;
using Forge.Domain.Analytics;
using Forge.Domain.Measurement;
using Forge.Domain.Recovery;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class SleepPerformancePairingTests
{
    private static readonly Guid ExerciseId = Guid.CreateVersion7();
    private static readonly Guid SessionId = Guid.CreateVersion7();
    private static readonly DateOnly Start = new(2026, 8, 1);

    [Fact]
    public void Nothing_logged_yields_no_claim()
    {
        var insight = SleepPerformancePairing.Analyze([], []);

        insight.HasClaim.ShouldBeFalse();
        insight.PairedDays.ShouldBe(0);
        insight.PairedDaysStillNeeded.ShouldBe(SleepPerformanceAssociationAnalyzer.MinimumSampleSize);
    }

    [Fact]
    public void Rest_days_are_never_paired_as_zero_performance()
    {
        // Ten nights of sleep but only three training days. If the seven rest days were paired
        // as zero volume, the sample size would clear the minimum and the analyzer would report
        // a confident association manufactured entirely out of the days with no session.
        var nights = Enumerable.Range(0, 10)
            .Select(day => new SleepNight(Start.AddDays(day), day < 5 ? 5m : 8m))
            .ToArray();
        var sets = new[]
        {
            Set(100m, 5, Start),
            Set(100m, 5, Start.AddDays(6)),
            Set(100m, 5, Start.AddDays(7)),
        };

        var insight = SleepPerformancePairing.Analyze(nights, sets);

        insight.PairedDays.ShouldBe(3);
        insight.SleepNightsRecorded.ShouldBe(10);
        insight.TrainingDaysRecorded.ShouldBe(3);
        insight.HasClaim.ShouldBeFalse();
    }

    [Fact]
    public void Training_days_without_a_sleep_entry_are_not_paired_either()
    {
        var nights = new[] { new SleepNight(Start, 8m) };
        var sets = new[] { Set(100m, 5, Start), Set(100m, 5, Start.AddDays(1)) };

        SleepPerformancePairing.BuildSamples(nights, sets).Count.ShouldBe(1);
    }

    [Fact]
    public void Fewer_paired_days_than_the_minimum_makes_no_association_claim()
    {
        var nights = Enumerable.Range(0, 7).Select(day => new SleepNight(Start.AddDays(day), day < 4 ? 5m : 8m)).ToArray();
        var sets = Enumerable.Range(0, 7).Select(day => Set(100m, 5, Start.AddDays(day))).ToArray();

        var insight = SleepPerformancePairing.Analyze(nights, sets);

        insight.PairedDays.ShouldBe(7);
        insight.HasClaim.ShouldBeFalse();
        insight.PairedDaysStillNeeded.ShouldBe(1);
        insight.Message.ShouldContain(
            SleepPerformanceAssociationAnalyzer.MinimumSampleSize.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Reaching_the_minimum_produces_an_association_worded_as_an_association()
    {
        var nights = Enumerable.Range(0, 8).Select(day => new SleepNight(Start.AddDays(day), day < 4 ? 5m : 8m)).ToArray();
        var sets = Enumerable.Range(0, 8)
            .Select(day => Set(day < 4 ? 80m : 100m, 5, Start.AddDays(day)))
            .ToArray();

        var insight = SleepPerformancePairing.Analyze(nights, sets);

        insight.PairedDays.ShouldBe(8);
        insight.HasClaim.ShouldBeTrue();
        insight.PairedDaysStillNeeded.ShouldBe(0);
        insight.Message.ShouldContain("associated");
        insight.Message.ShouldContain("association only");
    }

    [Fact]
    public void Samples_on_only_one_side_of_the_threshold_still_make_no_claim()
    {
        var nights = Enumerable.Range(0, 10).Select(day => new SleepNight(Start.AddDays(day), 8m)).ToArray();
        var sets = Enumerable.Range(0, 10).Select(day => Set(100m, 5, Start.AddDays(day))).ToArray();

        var insight = SleepPerformancePairing.Analyze(nights, sets);

        insight.PairedDays.ShouldBe(10);
        insight.HasClaim.ShouldBeFalse();
    }

    [Fact]
    public void Warm_ups_do_not_contribute_to_the_performance_figure()
    {
        var nights = new[] { new SleepNight(Start, 8m) };
        var sets = new[] { Set(100m, 5, Start), Set(200m, 5, Start, isWarmUp: true) };

        SleepPerformancePairing.BuildSamples(nights, sets).Single().PerformanceValue.ShouldBe(500m);
    }

    [Fact]
    public void A_day_with_only_warm_ups_is_not_a_training_day()
    {
        var nights = new[] { new SleepNight(Start, 8m) };
        var sets = new[] { Set(200m, 5, Start, isWarmUp: true) };

        SleepPerformancePairing.BuildSamples(nights, sets).ShouldBeEmpty();
        SleepPerformancePairing.Analyze(nights, sets).TrainingDaysRecorded.ShouldBe(0);
    }

    [Fact]
    public void Sleep_entries_without_hours_are_ignored()
    {
        var nights = new[] { new SleepNight(Start, 0m), new SleepNight(Start.AddDays(1), 8m) };
        var sets = new[] { Set(100m, 5, Start), Set(100m, 5, Start.AddDays(1)) };

        var insight = SleepPerformancePairing.Analyze(nights, sets);

        insight.SleepNightsRecorded.ShouldBe(1);
        insight.PairedDays.ShouldBe(1);
    }

    [Fact]
    public void Duplicate_entries_for_one_morning_are_averaged_into_a_single_sample()
    {
        var nights = new[] { new SleepNight(Start, 6m), new SleepNight(Start, 8m) };
        var sets = new[] { Set(100m, 5, Start) };

        var sample = SleepPerformancePairing.BuildSamples(nights, sets).Single();

        sample.SleepHours.ShouldBe(7m);
    }

    [Fact]
    public void Samples_come_back_in_date_order()
    {
        var nights = new[]
        {
            new SleepNight(Start.AddDays(2), 8m),
            new SleepNight(Start, 6m),
            new SleepNight(Start.AddDays(1), 7m),
        };
        var sets = Enumerable.Range(0, 3).Select(day => Set(100m, 5, Start.AddDays(day))).ToArray();

        SleepPerformancePairing.BuildSamples(nights, sets)
            .Select(sample => sample.Date)
            .ShouldBeInOrder();
    }

    [Fact]
    public void The_non_causation_caveat_says_so_plainly_and_names_a_confound()
    {
        SleepPerformancePairing.NonCausationCaveat.ShouldContain("not causation");
        SleepPerformancePairing.NonCausationCaveat.ShouldContain("controlled experiment");
        SleepPerformancePairing.PerformanceMeasureCaveat.ShouldContain("working volume");
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => SleepPerformancePairing.Analyze(null!, []));
        Should.Throw<ArgumentNullException>(() => SleepPerformancePairing.BuildSamples([], null!));
    }

    private static SetEntry Set(decimal kilograms, int reps, DateOnly localDate, bool isWarmUp = false)
        => new()
        {
            WorkoutSessionId = SessionId,
            ExerciseId = ExerciseId,
            Ordinal = 1,
            Load = Mass.FromKilograms(kilograms),
            Repetitions = reps,
            // Built from a local instant so the pairing date does not shift with the test machine.
            CompletedUtc = new DateTimeOffset(localDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Local)),
            IsWarmUp = isWarmUp
        };
}
