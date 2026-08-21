using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

public sealed class ExerciseFilterTests
{
    private static readonly Exercise GobletSquat = TestExercise.Create(
        "Goblet Squat", MovementPattern.Squat, "Quadriceps", ["Glutes"], "Dumbbell");

    private static readonly Exercise PushUp = TestExercise.Create(
        "Push Up", MovementPattern.Push, "Chest", ["Triceps"], difficulty: ExerciseDifficulty.Beginner);

    private static readonly Exercise PullUp = TestExercise.Create(
        "Pull Up", MovementPattern.Pull, "Lats", ["Biceps"], "Pull-up bar", ExerciseDifficulty.Advanced);

    [Fact]
    public void An_empty_filter_accepts_everything()
    {
        ExerciseFilter.None.IsEmpty.ShouldBeTrue();
        ExerciseFilter.None.Matches(GobletSquat).ShouldBeTrue();
        ExerciseFilter.None.Matches(PullUp).ShouldBeTrue();
        ExerciseFilter.None.ActiveCriteriaCount.ShouldBe(0);
    }

    [Fact]
    public void Values_within_one_axis_are_combined_with_or()
    {
        var filter = ExerciseFilter.For(equipment: ["Dumbbell", "Bodyweight"]);

        filter.Matches(GobletSquat).ShouldBeTrue();
        filter.Matches(PushUp).ShouldBeTrue();
        filter.Matches(PullUp).ShouldBeFalse();
        filter.ActiveCriteriaCount.ShouldBe(2);
    }

    [Fact]
    public void Different_axes_are_combined_with_and()
    {
        var filter = ExerciseFilter.For(
            muscles: ["Glutes"],
            equipment: ["Dumbbell"],
            patterns: [MovementPattern.Squat],
            difficulties: [ExerciseDifficulty.Beginner]);

        filter.Matches(GobletSquat).ShouldBeTrue();

        // Same muscle, wrong equipment.
        filter.Matches(TestExercise.Create("Hip Thrust", MovementPattern.Squat, "Glutes", equipment: "Barbell"))
            .ShouldBeFalse();
    }

    [Fact]
    public void Secondary_muscles_count_as_a_muscle_match()
    {
        ExerciseFilter.For(muscles: ["glutes"]).Matches(GobletSquat).ShouldBeTrue();
        ExerciseFilter.For(muscles: ["Quadriceps"]).Matches(GobletSquat).ShouldBeTrue();
        ExerciseFilter.For(muscles: ["Lats"]).Matches(GobletSquat).ShouldBeFalse();
    }

    [Fact]
    public void A_movement_needing_nothing_is_filterable_as_bodyweight()
        => ExerciseFilter.For(equipment: ["bodyweight"]).Matches(PushUp).ShouldBeTrue();

    [Fact]
    public void Declared_injuries_exclude_contraindicated_movement_patterns()
    {
        var filter = ExerciseFilter.FromDeclaredInjuries(["knee"]);

        filter.Matches(GobletSquat).ShouldBeFalse();
        filter.Matches(PullUp).ShouldBeTrue();
        filter.ExcludedMovements.ShouldContain(MovementPattern.Squat);
        filter.ExcludedMovements.ShouldContain(MovementPattern.Lunge);
    }

    [Fact]
    public void An_unrecognised_injury_excludes_nothing_rather_than_everything()
    {
        var filter = ExerciseFilter.FromDeclaredInjuries(["sore ego"]);

        filter.ExcludedMovements.ShouldBeEmpty();
        filter.Matches(GobletSquat).ShouldBeTrue();
    }

    [Fact]
    public void Scope_narrows_the_library_to_favourites_recents_or_custom_movements()
    {
        var favourite = TestExercise.Create("Front Squat", MovementPattern.Squat).Favourite();
        var recent = TestExercise.Create("Step Up", MovementPattern.Lunge).UsedAt(DateTimeOffset.UnixEpoch);
        var custom = TestExercise.Create("My Warm Up", MovementPattern.Mobility, isUserCreated: true);

        var favourites = ExerciseFilter.For(scope: ExerciseScope.Favourites);
        favourites.Matches(favourite).ShouldBeTrue();
        favourites.Matches(recent).ShouldBeFalse();
        favourites.ActiveCriteriaCount.ShouldBe(1);

        var recents = ExerciseFilter.For(scope: ExerciseScope.RecentlyUsed);
        recents.Matches(recent).ShouldBeTrue();
        recents.Matches(favourite).ShouldBeFalse();

        var userCreated = ExerciseFilter.For(scope: ExerciseScope.UserCreated);
        userCreated.Matches(custom).ShouldBeTrue();
        userCreated.Matches(favourite).ShouldBeFalse();
    }

    [Fact]
    public void Injuries_can_be_combined_with_ordinary_criteria()
    {
        var filter = ExerciseFilter.For(
            equipment: ["Bodyweight", "Dumbbell"],
            injuries: ["knee"]);

        filter.Matches(GobletSquat).ShouldBeFalse();
        filter.Matches(PushUp).ShouldBeTrue();
        filter.IsEmpty.ShouldBeFalse();
    }
}
