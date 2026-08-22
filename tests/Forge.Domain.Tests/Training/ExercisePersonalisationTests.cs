using Forge.Domain.Profile;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

/// <summary>
/// Favourites and recency, which belong to one profile even though the exercise does not.
/// </summary>
/// <remarks>
/// These used to be columns on the shared catalogue row, so on a shared device one person's
/// favourites were everybody's. The state now lives on <see cref="ExerciseProfileState"/> and is
/// attached to the exercise when it is read, which is what lets the filtering and ranking code
/// keep reading it off <see cref="Exercise"/> without knowing where it came from.
/// </remarks>
public sealed class ExercisePersonalisationTests
{
    private static readonly Guid Avery = Guid.CreateVersion7();
    private static readonly Guid Blake = Guid.CreateVersion7();

    [Fact]
    public void An_exercise_read_without_profile_state_is_nobodys_favourite()
    {
        // The workout summary and history resolve exercise names by identifier and never attach
        // state. Reporting false rather than throwing is what keeps those paths working, and it is
        // the honest answer to "is this pinned by nobody in particular".
        var exercise = new Exercise { Name = "Push Up" };

        exercise.IsFavourite.ShouldBeFalse();
        exercise.LastUsedUtc.ShouldBeNull();
    }

    [Fact]
    public void Favourite_state_comes_from_the_reading_profile_not_the_catalogue_row()
    {
        var exercise = new Exercise { Name = "Push Up" };
        var averys = ExerciseProfileState.Empty(Avery, exercise.Id);
        averys.IsFavourite = true;

        exercise.ApplyProfileState(averys);
        exercise.IsFavourite.ShouldBeTrue();
        exercise.IsUserCreated.ShouldBeFalse("pinning a catalogue movement must not make it look user-created");

        // The same shared row, read by somebody else, is not pinned.
        exercise.ApplyProfileState(ExerciseProfileState.Empty(Blake, exercise.Id));
        exercise.IsFavourite.ShouldBeFalse();
    }

    [Fact]
    public void Last_used_time_records_recent_library_interaction_per_profile()
    {
        var usedUtc = new DateTimeOffset(2026, 8, 20, 20, 45, 0, TimeSpan.Zero);
        var exercise = new Exercise { Name = "Goblet Squat" };

        var averys = ExerciseProfileState.Empty(Avery, exercise.Id);
        averys.LastUsedUtc = usedUtc;
        exercise.ApplyProfileState(averys);

        exercise.LastUsedUtc.ShouldBe(usedUtc);

        exercise.ApplyProfileState(ExerciseProfileState.Empty(Blake, exercise.Id));
        exercise.LastUsedUtc.ShouldBeNull("somebody else's visit is not this profile's history");
    }

    [Fact]
    public void The_state_is_owned_and_therefore_scopable_and_deletable()
    {
        var state = ExerciseProfileState.Empty(Avery, Guid.CreateVersion7());

        state.ShouldBeAssignableTo<IProfileOwned>();
        new ProfileScope(Avery).Owns(state).ShouldBeTrue();
        new ProfileScope(Blake).Owns(state).ShouldBeFalse();
        ProfileScope.None.Owns(state).ShouldBeFalse();
    }

    [Fact]
    public void Empty_state_carries_no_opinion()
    {
        // A row is written only once somebody expresses something. Seeding one per profile per
        // catalogue entry would multiply the shipped catalogue by the profile count for no
        // information at all.
        var state = ExerciseProfileState.Empty(Avery, Guid.CreateVersion7());

        state.IsFavourite.ShouldBeFalse();
        state.LastUsedUtc.ShouldBeNull();
    }
}
