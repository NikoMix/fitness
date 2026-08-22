using System.Text.Json;
using Forge.Domain.Measurement;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

/// <summary>
/// The exercise queue and the completed sets are stored as JSON inside the recoverable snapshot,
/// so a serialisation change is a data-loss change: a queue that fails to round-trip loses the
/// user's supersets and rest settings the moment the app is killed mid-workout.
/// </summary>
public sealed class ActiveWorkoutStateSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly DateTimeOffset Start = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_queued_exercise_round_trips_with_its_superset_and_rest_settings()
    {
        var groupId = Guid.CreateVersion7();
        var original = new ActiveWorkoutExercise(
            Guid.CreateVersion7(),
            "Back squat",
            "Quads",
            102.5m,
            5,
            groupId,
            RestPrescription.FromWorkingSetRest(TimeSpan.FromMinutes(4)));

        var restored = JsonSerializer.Deserialize<ActiveWorkoutExercise>(
            JsonSerializer.Serialize(original, Options),
            Options);

        restored.ShouldBe(original);
        restored!.SupersetGroupId.ShouldBe(groupId);
        restored.Rest!.WorkingSetRest.ShouldBe(TimeSpan.FromMinutes(4));
        restored.Rest.WarmUpRest.ShouldBe(TimeSpan.FromMinutes(2));
    }
    [Fact]
    public void An_exercise_stored_before_supersets_existed_still_loads()
    {
        const string legacyJson = """
            {"exerciseId":"0195b0d0-0000-7000-8000-000000000001","name":"Bench press",
             "primaryMuscle":"Chest","targetLoadKilograms":80,"targetRepetitions":8}
            """;

        var restored = JsonSerializer.Deserialize<ActiveWorkoutExercise>(legacyJson, Options);

        restored.ShouldNotBeNull();
        restored.Name.ShouldBe("Bench press");
        restored.SupersetGroupId.ShouldBeNull();
        restored.Rest.ShouldBeNull();
    }

    [Fact]
    public void An_exercise_without_a_stored_rest_setting_falls_back_to_the_app_default()
    {
        var exercise = new ActiveWorkoutExercise(Guid.CreateVersion7(), "Cable curl", "Biceps", 20m, 12);
        var state = ActiveWorkoutState.Start(Owner, Guid.CreateVersion7(), Start, exercise);

        state.ResolveNextRest(isWarmUp: false)!.Duration.ShouldBe(RestPrescription.Default.WorkingSetRest);
    }

    [Fact]
    public void A_completed_set_round_trips_including_its_identity()
    {
        var state = ActiveWorkoutState.Start(
            Owner,
            Guid.CreateVersion7(),
            Start,
            new ActiveWorkoutExercise(Guid.CreateVersion7(), "Deadlift", "Posterior chain", 140m, 3));
        var original = state.LogSet(Mass.FromKilograms(142.5m), 3, false, true, 0, Start.AddMinutes(5), "Posterior chain");

        var restored = JsonSerializer.Deserialize<CompletedWorkoutSet>(
            JsonSerializer.Serialize(original, Options),
            Options);

        restored.ShouldBe(original);
        restored!.SetEntryId.ShouldBe(original.SetEntryId);
        restored.LoadKilograms.ShouldBe(142.5m);
    }
}
