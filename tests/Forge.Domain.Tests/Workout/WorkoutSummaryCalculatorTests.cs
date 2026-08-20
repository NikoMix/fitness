using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class WorkoutSummaryCalculatorTests
{
    [Fact]
    public void Summary_counts_working_volume_by_primary_muscle_and_records()
    {
        var exercise = new Exercise { Name = "Back squat", PrimaryMuscle = "Quads" };
        var session = new WorkoutSession
        {
            StartedUtc = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero),
            CompletedUtc = new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.Zero)
        };
        session.Sets.Add(new SetEntry { WorkoutSessionId = session.Id, ExerciseId = exercise.Id, Ordinal = 1, Load = Mass.FromKilograms(60m), Repetitions = 5, IsWarmUp = true });
        session.Sets.Add(new SetEntry { WorkoutSessionId = session.Id, ExerciseId = exercise.Id, Ordinal = 2, Load = Mass.FromKilograms(100m), Repetitions = 5 });

        var previous = new[] { new SetEntry { WorkoutSessionId = Guid.CreateVersion7(), ExerciseId = exercise.Id, Ordinal = 1, Load = Mass.FromKilograms(95m), Repetitions = 5 } };
        var summary = WorkoutSummaryCalculator.Calculate(session, new Dictionary<Guid, Exercise> { [exercise.Id] = exercise }, session.CompletedUtc.Value, previous);

        summary.WorkingSetCount.ShouldBe(1);
        summary.TotalVolume.Kilograms.ShouldBe(500m);
        summary.Duration.ShouldBe(TimeSpan.FromHours(1));
        summary.PerMuscleVolume["Quads"].Kilograms.ShouldBe(500m);
        summary.PersonalRecords.ShouldContain(r => r.Kind == PersonalRecordKind.HeaviestLoad && r.CurrentValue == 100m);
    }
}
