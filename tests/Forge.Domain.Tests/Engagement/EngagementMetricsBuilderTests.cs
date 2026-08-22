using Forge.Domain.Engagement;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Engagement;

/// <summary>
/// The counts the achievement rules are allowed to read.
/// </summary>
/// <remarks>
/// The progression tests carry the ethical weight. The rule has to reward getting stronger over
/// weeks while refusing to reward a single maximal effort, and those two cases are only a few
/// rows of data apart, so both are asserted explicitly.
/// </remarks>
public sealed class EngagementMetricsBuilderTests
{
    private static readonly Guid Squat = Guid.CreateVersion7();
    private static readonly Guid Bench = Guid.CreateVersion7();
    private static readonly DateOnly Start = new(2026, 6, 1);

    private static TrainingRhythm Rhythm(IEnumerable<DateOnly> dates, DateOnly today, int target = 3)
        => TrainingRhythmAnalyzer.Analyze(dates, today, target, []);

    private static EngagementSet Set(Guid exercise, int dayOffset, decimal kilograms, int reps, bool effort = false, MovementPattern pattern = MovementPattern.Squat)
        => new(exercise, Start.AddDays(dayOffset), pattern, Mass.FromKilograms(kilograms), reps, effort);

    [Fact]
    public void A_profile_with_nothing_logged_measures_zero_everywhere()
    {
        var metrics = EngagementMetricsBuilder.Build(Rhythm([], Start), [], [], 0);

        metrics.CompletedSessions.ShouldBe(0);
        metrics.TotalActiveWeeks.ShouldBe(0);
        metrics.DistinctMovementPatterns.ShouldBe(0);
        metrics.ExercisesProgressingGradually.ShouldBe(0);
        metrics.ReturnedAfterBreak.ShouldBeFalse();
        metrics.TookLighterWeekAfterHardBlock.ShouldBeFalse();
    }

    [Fact]
    public void Unspecified_movement_patterns_are_not_counted_as_variety()
    {
        EngagementSet[] sets =
        [
            Set(Squat, 0, 100, 5, pattern: MovementPattern.Squat),
            Set(Bench, 0, 60, 5, pattern: MovementPattern.Unspecified),
            Set(Bench, 1, 60, 5, pattern: MovementPattern.Push),
        ];

        var metrics = EngagementMetricsBuilder.Build(Rhythm([Start], Start), [Start], sets, 0);

        metrics.DistinctMovementPatterns.ShouldBe(2);
    }

    [Fact]
    public void Effort_is_counted_only_when_the_user_actually_recorded_it()
    {
        EngagementSet[] sets = [Set(Squat, 0, 100, 5, effort: true), Set(Squat, 0, 100, 5), Set(Squat, 1, 100, 5, effort: true)];

        var metrics = EngagementMetricsBuilder.Build(Rhythm([Start], Start), [Start], sets, 0);

        metrics.SetsWithEffortRecorded.ShouldBe(2);
    }

    [Fact]
    public void Returning_after_a_two_week_gap_is_detected_anywhere_in_the_history()
    {
        DateOnly[] dates = [Start, Start.AddDays(2), Start.AddDays(30), Start.AddDays(32)];

        var metrics = EngagementMetricsBuilder.Build(Rhythm(dates, Start.AddDays(40)), dates, [], 0);

        metrics.ReturnedAfterBreak.ShouldBeTrue();
    }

    [Fact]
    public void An_ordinary_rest_gap_is_not_a_return_after_a_break()
    {
        DateOnly[] dates = [Start, Start.AddDays(3), Start.AddDays(7), Start.AddDays(10)];

        var metrics = EngagementMetricsBuilder.Build(Rhythm(dates, Start.AddDays(12)), dates, [], 0);

        metrics.ReturnedAfterBreak.ShouldBeFalse();
    }

    [Fact]
    public void Returning_stays_detected_once_it_has_happened()
    {
        // Earned recognition that could later be withdrawn would only ever be withdrawn from
        // somebody in the middle of a break, which is the worst possible moment.
        DateOnly[] dates = [Start, Start.AddDays(30), Start.AddDays(33), Start.AddDays(36), Start.AddDays(40)];

        var metrics = EngagementMetricsBuilder.Build(Rhythm(dates, Start.AddDays(42)), dates, [], 0);

        metrics.ReturnedAfterBreak.ShouldBeTrue();
    }

    [Fact]
    public void A_lighter_week_after_three_weeks_at_target_reads_as_a_deload()
    {
        // Three finished weeks at a target of two, then a finished week with one session.
        List<DateOnly> dates = [];
        for (var week = 0; week < 3; week++)
        {
            dates.Add(Start.AddDays((week * 7) + 0));
            dates.Add(Start.AddDays((week * 7) + 3));
        }

        dates.Add(Start.AddDays(21));

        var today = Start.AddDays(35);
        var metrics = EngagementMetricsBuilder.Build(Rhythm(dates, today, 2), dates, [], 0);

        metrics.TookLighterWeekAfterHardBlock.ShouldBeTrue();
    }

    [Fact]
    public void A_lighter_week_with_no_hard_block_before_it_is_not_a_deload()
    {
        List<DateOnly> dates = [Start, Start.AddDays(3), Start.AddDays(7)];

        var metrics = EngagementMetricsBuilder.Build(Rhythm(dates, Start.AddDays(21), 2), dates, [], 0);

        metrics.TookLighterWeekAfterHardBlock.ShouldBeFalse();
    }

    [Fact]
    public void Strength_rising_repeatedly_over_weeks_counts_as_gradual_progression()
    {
        EngagementSet[] sets =
        [
            Set(Squat, 0, 100, 5),
            Set(Squat, 7, 102.5m, 5),
            Set(Squat, 14, 105, 5),
            Set(Squat, 21, 107.5m, 5),
            Set(Squat, 28, 110, 5),
        ];

        var metrics = EngagementMetricsBuilder.Build(Rhythm([Start], Start.AddDays(30)), [Start], sets, 0);

        metrics.ExercisesProgressingGradually.ShouldBe(1);
    }

    [Fact]
    public void One_very_heavy_session_is_not_progression()
    {
        // The badge must not be reachable by testing a maximum. A single jump improves the
        // running best exactly once, however large the jump is.
        EngagementSet[] sets =
        [
            Set(Squat, 0, 100, 5),
            Set(Squat, 7, 100, 5),
            Set(Squat, 14, 100, 5),
            Set(Squat, 21, 100, 5),
            Set(Squat, 28, 160, 1),
        ];

        var metrics = EngagementMetricsBuilder.Build(Rhythm([Start], Start.AddDays(30)), [Start], sets, 0);

        metrics.ExercisesProgressingGradually.ShouldBe(0);
    }

    [Fact]
    public void Improvements_crammed_into_a_fortnight_are_not_gradual()
    {
        EngagementSet[] sets =
        [
            Set(Squat, 0, 100, 5),
            Set(Squat, 2, 105, 5),
            Set(Squat, 4, 110, 5),
            Set(Squat, 6, 115, 5),
        ];

        var metrics = EngagementMetricsBuilder.Build(Rhythm([Start], Start.AddDays(10)), [Start], sets, 0);

        metrics.ExercisesProgressingGradually.ShouldBe(0);
    }

    [Fact]
    public void Sets_the_estimator_cannot_support_are_ignored_rather_than_extrapolated()
    {
        // Twenty repetitions is outside every published one-rep-max fit. Counting it would make a
        // strength claim that no formula backs.
        EngagementSet[] sets =
        [
            Set(Squat, 0, 60, 20),
            Set(Squat, 7, 65, 20),
            Set(Squat, 14, 70, 20),
            Set(Squat, 21, 75, 20),
            Set(Squat, 28, 80, 20),
        ];

        var metrics = EngagementMetricsBuilder.Build(Rhythm([Start], Start.AddDays(30)), [Start], sets, 0);

        metrics.ExercisesProgressingGradually.ShouldBe(0);
    }

    [Fact]
    public void Bodyweight_sets_with_no_load_do_not_produce_a_strength_claim()
    {
        EngagementSet[] sets =
        [
            Set(Squat, 0, 0, 10),
            Set(Squat, 7, 0, 12),
            Set(Squat, 14, 0, 14),
            Set(Squat, 21, 0, 16),
            Set(Squat, 28, 0, 18),
        ];

        var metrics = EngagementMetricsBuilder.Build(Rhythm([Start], Start.AddDays(30)), [Start], sets, 0);

        metrics.ExercisesProgressingGradually.ShouldBe(0);
    }

    [Fact]
    public void Progression_is_counted_per_exercise()
    {
        EngagementSet[] sets =
        [
            Set(Squat, 0, 100, 5), Set(Squat, 7, 102.5m, 5), Set(Squat, 14, 105, 5), Set(Squat, 21, 107.5m, 5),
            Set(Bench, 0, 60, 5), Set(Bench, 7, 62.5m, 5), Set(Bench, 14, 65, 5), Set(Bench, 21, 67.5m, 5),
        ];

        var metrics = EngagementMetricsBuilder.Build(Rhythm([Start], Start.AddDays(25)), [Start], sets, 0);

        metrics.ExercisesProgressingGradually.ShouldBe(2);
    }

    [Fact]
    public void Recovery_check_ins_are_taken_as_given_and_cannot_be_negative()
    {
        var metrics = EngagementMetricsBuilder.Build(Rhythm([Start], Start), [Start], [], 12);

        metrics.RecoveryCheckIns.ShouldBe(12);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            EngagementMetricsBuilder.Build(Rhythm([Start], Start), [Start], [], -1));
    }

    [Fact]
    public void Weeks_that_contained_training_are_counted_across_the_whole_history()
    {
        DateOnly[] dates = [Start, Start.AddDays(21), Start.AddDays(42)];

        var metrics = EngagementMetricsBuilder.Build(Rhythm(dates, Start.AddDays(44)), dates, [], 0);

        // Three separated weeks contained training even though no two were adjacent.
        metrics.TotalActiveWeeks.ShouldBe(3);
        metrics.ActiveWeeks.ShouldBe(1);
    }
}
