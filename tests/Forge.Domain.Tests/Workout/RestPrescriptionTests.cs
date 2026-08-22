using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class RestPrescriptionTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Fact]
    public void Default_prescription_shortens_warm_up_rest()
    {
        var prescription = RestPrescription.Default;

        prescription.Resolve(RestReason.WorkingSet).ShouldBe(TimeSpan.FromMinutes(2));
        prescription.Resolve(RestReason.WarmUpSet).ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Closing_a_superset_round_earns_the_full_working_rest()
    {
        var prescription = RestPrescription.Default;

        prescription.Resolve(RestReason.SupersetRound).ShouldBe(prescription.Resolve(RestReason.WorkingSet));
    }

    [Fact]
    public void Deriving_from_a_working_rest_halves_the_warm_up()
    {
        var prescription = RestPrescription.FromWorkingSetRest(TimeSpan.FromMinutes(4));

        prescription.WorkingSetRest.ShouldBe(TimeSpan.FromMinutes(4));
        prescription.WarmUpRest.ShouldBe(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void Absurd_durations_are_clamped_rather_than_accepted()
    {
        var tooLong = RestPrescription.FromWorkingSetRest(TimeSpan.FromHours(3));
        var tooShort = new RestPrescription(TimeSpan.Zero, TimeSpan.FromSeconds(-30));

        tooLong.WorkingSetRest.ShouldBe(RestPrescription.MaximumRest);
        tooShort.WorkingSetRest.ShouldBe(RestPrescription.MinimumRest);
        tooShort.WarmUpRest.ShouldBe(RestPrescription.MinimumRest);
    }

    [Fact]
    public void Per_exercise_prescription_overrides_the_app_default()
    {
        var squat = new ActiveWorkoutExercise(
            Guid.CreateVersion7(),
            "Back squat",
            "Quads",
            100m,
            5,
            Rest: RestPrescription.FromWorkingSetRest(TimeSpan.FromMinutes(5)));
        var state = ActiveWorkoutState.Start(Owner, Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, squat);

        var next = state.ResolveNextRest(isWarmUp: false);

        next.ShouldNotBeNull();
        next.Reason.ShouldBe(RestReason.WorkingSet);
        next.Duration.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Exercise_without_a_prescription_falls_back_to_the_supplied_default()
    {
        var curl = new ActiveWorkoutExercise(Guid.CreateVersion7(), "Cable curl", "Biceps", 20m, 12);
        var state = ActiveWorkoutState.Start(Owner, Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, curl);

        var next = state.ResolveNextRest(isWarmUp: false, RestPrescription.FromWorkingSetRest(TimeSpan.FromSeconds(45)));

        next!.Duration.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Warm_up_sets_get_the_shorter_warm_up_rest()
    {
        var press = new ActiveWorkoutExercise(
            Guid.CreateVersion7(),
            "Bench press",
            "Chest",
            80m,
            5,
            Rest: RestPrescription.FromWorkingSetRest(TimeSpan.FromMinutes(3)));
        var state = ActiveWorkoutState.Start(Owner, Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, press);

        var next = state.ResolveNextRest(isWarmUp: true);

        next!.Reason.ShouldBe(RestReason.WarmUpSet);
        next.Duration.ShouldBe(TimeSpan.FromSeconds(90));
    }
}
