using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

public sealed class ExercisePersonalisationTests
{
    [Fact]
    public void Favourite_marker_can_be_toggled_without_marking_catalogue_item_as_custom()
    {
        var exercise = new Exercise { Name = "Push Up" };

        exercise.SetFavourite(true);

        exercise.IsFavourite.ShouldBeTrue();
        exercise.IsUserCreated.ShouldBeFalse();

        exercise.SetFavourite(false);

        exercise.IsFavourite.ShouldBeFalse();
    }

    [Fact]
    public void Last_used_time_records_recent_library_interaction()
    {
        var usedUtc = new DateTimeOffset(2026, 8, 20, 20, 45, 0, TimeSpan.Zero);
        var exercise = new Exercise { Name = "Goblet Squat" };

        exercise.MarkUsed(usedUtc);

        exercise.LastUsedUtc.ShouldBe(usedUtc);
    }
}
