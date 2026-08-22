using Forge.Infrastructure.Media;
using Shouldly;

namespace Forge.Infrastructure.Tests.Media;

/// <summary>
/// Guards the asset name the app derives, which is a contract with whoever encodes the packs.
/// </summary>
/// <remarks>
/// Nothing at runtime can detect a mismatch here: an asset the app asks for under the wrong name
/// simply is not found, and the screen correctly reports no video. It would look exactly like a
/// pack that had not been downloaded.
/// </remarks>
public sealed class MediaAssetKeysTests
{
    [Theory]
    [InlineData("Bodyweight Squat", "bodyweight-squat.mp4")]
    [InlineData("Push Up", "push-up.mp4")]
    [InlineData("Cable Pull Through", "cable-pull-through.mp4")]
    [InlineData("World's Greatest Stretch", "worlds-greatest-stretch.mp4")]
    [InlineData("Half Kneeling Hip Flexor Stretch", "half-kneeling-hip-flexor-stretch.mp4")]
    public void An_exercise_name_becomes_a_readable_slug(string exerciseName, string expected)
        => MediaAssetKeys.FileNameForExercise(exerciseName).ShouldBe(expected);

    [Fact]
    public void Surrounding_and_repeated_separators_do_not_survive()
        => MediaAssetKeys.FileNameForExercise("  Dead Bug   With  Reach ")
            .ShouldBe("dead-bug-with-reach.mp4");

    [Fact]
    public void Casing_does_not_change_the_file_asked_for()
        => MediaAssetKeys.FileNameForExercise("GOBLET squat")
            .ShouldBe(MediaAssetKeys.FileNameForExercise("Goblet Squat"));

    [Fact]
    public void A_blank_name_is_rejected_rather_than_producing_a_nameless_asset()
        => Should.Throw<ArgumentException>(() => MediaAssetKeys.FileNameForExercise("  "));
}
