using Forge.Domain.Analytics;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class VolumeAggregatorTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Fact]
    public void Empty_sets_produce_empty_aggregates()
    {
        VolumeAggregator.PerWeek([]).ShouldBeEmpty();
        VolumeAggregator.PerMuscleGroup([], []).ShouldBeEmpty();
        VolumeAggregator.PerMovementPattern([], []).ShouldBeEmpty();
    }

    [Fact]
    public void Weekly_volume_excludes_warm_ups_and_starts_on_monday()
    {
        var exerciseId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var sets = new[]
        {
            Set(exerciseId, sessionId, 100m, 5, new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero)),
            Set(exerciseId, sessionId, 200m, 5, new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), isWarmUp: true),
            Set(exerciseId, sessionId, 80m, 5, new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero)),
        };

        var weeks = VolumeAggregator.PerWeek(sets);

        weeks.Count.ShouldBe(2);
        weeks[0].WeekStarting.ShouldBe(new DateOnly(2026, 8, 17));
        weeks[0].Volume.Kilograms.ShouldBe(500m);
        weeks[1].WeekStarting.ShouldBe(new DateOnly(2026, 8, 24));
        weeks[1].Volume.Kilograms.ShouldBe(400m);
    }

    [Fact]
    public void Aggregates_by_muscle_group_and_movement_pattern()
    {
        var squatId = Guid.CreateVersion7();
        var rowId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var exercises = new[]
        {
            new Exercise { Id = squatId, Name = "Squat", PrimaryMuscle = "Quads", SecondaryMuscles = ["Glutes"], Pattern = MovementPattern.Squat },
            new Exercise { Id = rowId, Name = "Row", PrimaryMuscle = "Back", Pattern = MovementPattern.Pull },
        };
        var sets = new[]
        {
            Set(squatId, sessionId, 100m, 5, DateTimeOffset.UtcNow),
            Set(rowId, sessionId, 50m, 10, DateTimeOffset.UtcNow),
        };

        VolumeAggregator.PerMuscleGroup(sets, exercises).ShouldContain(volume => volume.MuscleGroup == "Quads" && volume.Volume.Kilograms == 500m);
        VolumeAggregator.PerMuscleGroup(sets, exercises).ShouldContain(volume => volume.MuscleGroup == "Glutes" && volume.Volume.Kilograms == 500m);
        VolumeAggregator.PerMovementPattern(sets, exercises).ShouldContain(volume => volume.Pattern == MovementPattern.Pull && volume.Volume.Kilograms == 500m);
    }

    private static SetEntry Set(Guid exerciseId, Guid sessionId, decimal kilograms, int reps, DateTimeOffset completed, bool isWarmUp = false)
        => new()
        {
            UserProfileId = Owner,
            WorkoutSessionId = sessionId,
            ExerciseId = exerciseId,
            Ordinal = 1,
            Load = Mass.FromKilograms(kilograms),
            Repetitions = reps,
            CompletedUtc = completed,
            IsWarmUp = isWarmUp
        };
}
