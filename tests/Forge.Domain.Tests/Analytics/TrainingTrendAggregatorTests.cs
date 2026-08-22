using Forge.Domain.Analytics;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class TrainingTrendAggregatorTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid SessionId = Guid.CreateVersion7();

    [Fact]
    public void No_sets_produce_no_weeks_and_no_slices()
    {
        TrainingTrendAggregator.PerWeek([]).ShouldBeEmpty();
        TrainingTrendAggregator.PerWeekByMuscleGroup([], []).ShouldBeEmpty();
        TrainingTrendAggregator.PerWeekByMovementPattern([], []).ShouldBeEmpty();
    }

    [Fact]
    public void Warm_ups_and_zero_rep_sets_are_excluded_entirely()
    {
        var exerciseId = Guid.CreateVersion7();
        var sets = new[]
        {
            Set(exerciseId, 100m, 5, LocalNoon(2026, 8, 19)),
            Set(exerciseId, 200m, 5, LocalNoon(2026, 8, 19), isWarmUp: true),
            Set(exerciseId, 300m, 0, LocalNoon(2026, 8, 19)),
        };

        var weeks = TrainingTrendAggregator.PerWeek(sets);

        weeks.Count.ShouldBe(1);
        weeks[0].WorkingSets.ShouldBe(1);
        weeks[0].Repetitions.ShouldBe(5);
        weeks[0].Volume.Kilograms.ShouldBe(500m);
        weeks[0].HeaviestLoad.Kilograms.ShouldBe(100m);
    }

    [Fact]
    public void Weeks_start_on_the_local_monday()
    {
        var exerciseId = Guid.CreateVersion7();
        var sets = new[]
        {
            // Wednesday and the following Sunday belong to the same Monday-start week.
            Set(exerciseId, 100m, 5, LocalNoon(2026, 8, 19)),
            Set(exerciseId, 100m, 5, LocalNoon(2026, 8, 23)),
            Set(exerciseId, 80m, 5, LocalNoon(2026, 8, 24)),
        };

        var weeks = TrainingTrendAggregator.PerWeek(sets);

        weeks.Count.ShouldBe(2);
        weeks[0].WeekStarting.ShouldBe(new DateOnly(2026, 8, 17));
        weeks[0].WorkingSets.ShouldBe(2);
        weeks[1].WeekStarting.ShouldBe(new DateOnly(2026, 8, 24));
        weeks[1].WorkingSets.ShouldBe(1);
    }

    [Fact]
    public void Mean_load_is_repetition_weighted_rather_than_a_plain_average_of_loads()
    {
        var exerciseId = Guid.CreateVersion7();
        var sets = new[]
        {
            Set(exerciseId, 100m, 10, LocalNoon(2026, 8, 19)),
            Set(exerciseId, 50m, 2, LocalNoon(2026, 8, 19)),
        };

        var week = TrainingTrendAggregator.PerWeek(sets).Single();

        // A plain average of the two loads would be 75 kg, which over-weights the two-rep set.
        week.MeanLoad.Kilograms.ShouldBe(91.67m);
        week.Volume.Kilograms.ShouldBe(1100m);
        week.Repetitions.ShouldBe(12);
    }

    [Fact]
    public void Bodyweight_sets_count_toward_work_done_but_never_drag_the_intensity_signal_down()
    {
        var exerciseId = Guid.CreateVersion7();
        var sets = new[]
        {
            Set(exerciseId, 100m, 5, LocalNoon(2026, 8, 19)),
            Set(exerciseId, 0m, 20, LocalNoon(2026, 8, 19)),
        };

        var week = TrainingTrendAggregator.PerWeek(sets).Single();

        week.WorkingSets.ShouldBe(2);
        week.LoadedWorkingSets.ShouldBe(1);
        week.Repetitions.ShouldBe(25);
        week.Volume.Kilograms.ShouldBe(500m);

        // Averaging the unloaded reps in would report 20 kg and imply intensity had collapsed.
        week.MeanLoad.Kilograms.ShouldBe(100m);
        week.HeaviestLoad.Kilograms.ShouldBe(100m);
    }

    [Fact]
    public void A_week_of_only_bodyweight_work_reports_no_mean_load_rather_than_a_wrong_one()
    {
        var exerciseId = Guid.CreateVersion7();

        var week = TrainingTrendAggregator.PerWeek([Set(exerciseId, 0m, 20, LocalNoon(2026, 8, 19))]).Single();

        week.LoadedWorkingSets.ShouldBe(0);
        week.MeanLoad.ShouldBe(Mass.Zero);
        week.HeaviestLoad.ShouldBe(Mass.Zero);
        week.Repetitions.ShouldBe(20);
    }

    [Fact]
    public void A_set_counts_in_full_toward_every_muscle_its_exercise_trains()
    {
        var squatId = Guid.CreateVersion7();
        var exercises = new[]
        {
            new Exercise { Id = squatId, Name = "Squat", PrimaryMuscle = "Quads", SecondaryMuscles = ["Glutes"], Pattern = MovementPattern.Squat },
        };
        var sets = new[] { Set(squatId, 100m, 5, LocalNoon(2026, 8, 19)) };

        var slices = TrainingTrendAggregator.PerWeekByMuscleGroup(sets, exercises);

        slices.Count.ShouldBe(2);
        slices.ShouldAllBe(slice => slice.TotalVolume.Kilograms == 500m);

        // The overlap is intentional, and the caveat exists so the reader is told about it.
        slices.Sum(slice => slice.TotalVolume.Kilograms).ShouldBe(1000m);
        TrainingTrendAggregator.MuscleGroupOverlapCaveat.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_muscle_listed_as_both_primary_and_secondary_is_only_counted_once()
    {
        var exerciseId = Guid.CreateVersion7();
        var exercises = new[]
        {
            new Exercise { Id = exerciseId, Name = "Row", PrimaryMuscle = "Back", SecondaryMuscles = ["back", "Biceps"] },
        };
        var sets = new[] { Set(exerciseId, 60m, 10, LocalNoon(2026, 8, 19)) };

        var slices = TrainingTrendAggregator.PerWeekByMuscleGroup(sets, exercises);

        slices.Count.ShouldBe(2);
        slices.Single(slice => string.Equals(slice.Label, "Back", StringComparison.OrdinalIgnoreCase))
            .TotalVolume.Kilograms.ShouldBe(600m);
    }

    [Fact]
    public void Muscle_and_pattern_slices_carry_their_own_week_series()
    {
        var squatId = Guid.CreateVersion7();
        var exercises = new[]
        {
            new Exercise { Id = squatId, Name = "Squat", PrimaryMuscle = "Quads", Pattern = MovementPattern.Squat },
        };
        var sets = new[]
        {
            Set(squatId, 100m, 5, LocalNoon(2026, 8, 19)),
            Set(squatId, 120m, 5, LocalNoon(2026, 8, 26)),
        };

        var muscle = TrainingTrendAggregator.PerWeekByMuscleGroup(sets, exercises).Single();
        muscle.Weeks.Count.ShouldBe(2);
        muscle.Weeks[0].MeanLoad.Kilograms.ShouldBe(100m);
        muscle.Weeks[1].MeanLoad.Kilograms.ShouldBe(120m);

        var pattern = TrainingTrendAggregator.PerWeekByMovementPattern(sets, exercises).Single();
        pattern.Label.ShouldBe(MovementPattern.Squat.ToDisplayName());
        pattern.TotalWorkingSets.ShouldBe(2);
    }

    [Fact]
    public void Slices_are_ordered_by_volume_so_the_biggest_contributor_leads()
    {
        var squatId = Guid.CreateVersion7();
        var curlId = Guid.CreateVersion7();
        var exercises = new[]
        {
            new Exercise { Id = squatId, Name = "Squat", PrimaryMuscle = "Quads", Pattern = MovementPattern.Squat },
            new Exercise { Id = curlId, Name = "Curl", PrimaryMuscle = "Biceps", Pattern = MovementPattern.Pull },
        };
        var sets = new[]
        {
            Set(squatId, 100m, 5, LocalNoon(2026, 8, 19)),
            Set(curlId, 20m, 10, LocalNoon(2026, 8, 19)),
        };

        TrainingTrendAggregator.PerWeekByMuscleGroup(sets, exercises)[0].Label.ShouldBe("Quads");
        TrainingTrendAggregator.PerWeekByMovementPattern(sets, exercises)[0].Label
            .ShouldBe(MovementPattern.Squat.ToDisplayName());
    }

    [Fact]
    public void Sets_whose_exercise_is_missing_from_the_catalogue_are_left_out_of_slices()
    {
        var orphanId = Guid.CreateVersion7();
        var sets = new[] { Set(orphanId, 100m, 5, LocalNoon(2026, 8, 19)) };

        TrainingTrendAggregator.PerWeekByMuscleGroup(sets, []).ShouldBeEmpty();
        TrainingTrendAggregator.PerWeekByMovementPattern(sets, []).ShouldBeEmpty();

        // The whole-history series does not need the catalogue and still sees the work.
        TrainingTrendAggregator.PerWeek(sets).Single().Volume.Kilograms.ShouldBe(500m);
    }

    [Fact]
    public void A_duplicated_exercise_row_does_not_throw_when_building_the_catalogue()
    {
        var exerciseId = Guid.CreateVersion7();
        var exercises = new[]
        {
            new Exercise { Id = exerciseId, Name = "Squat", PrimaryMuscle = "Quads", Pattern = MovementPattern.Squat },
            new Exercise { Id = exerciseId, Name = "Squat", PrimaryMuscle = "Quads", Pattern = MovementPattern.Squat },
        };

        Should.NotThrow(() => TrainingTrendAggregator.PerWeekByMuscleGroup(
            [Set(exerciseId, 100m, 5, LocalNoon(2026, 8, 19))],
            exercises));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => TrainingTrendAggregator.PerWeek(null!));
        Should.Throw<ArgumentNullException>(() => TrainingTrendAggregator.PerWeekByMuscleGroup(null!, []));
        Should.Throw<ArgumentNullException>(() => TrainingTrendAggregator.PerWeekByMovementPattern([], null!));
    }

    /// <summary>
    /// Builds an instant that round-trips through the local calendar, so the week a set lands in
    /// does not depend on the timezone of the machine running the tests.
    /// </summary>
    private static DateTimeOffset LocalNoon(int year, int month, int day)
        => new(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Local));

    private static SetEntry Set(Guid exerciseId, decimal kilograms, int reps, DateTimeOffset completed, bool isWarmUp = false)
        => new()
        {
            UserProfileId = Owner,
            WorkoutSessionId = SessionId,
            ExerciseId = exerciseId,
            Ordinal = 1,
            Load = Mass.FromKilograms(kilograms),
            Repetitions = reps,
            CompletedUtc = completed,
            IsWarmUp = isWarmUp
        };
}
