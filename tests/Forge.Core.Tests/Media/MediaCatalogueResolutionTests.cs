using Forge.Core.Abstractions.Media;
using NSubstitute;
using Shouldly;

namespace Forge.Core.Tests.Media;

public sealed class MediaCatalogueResolutionTests
{
    [Fact]
    public async Task ResolveExerciseMediaAsync_returns_absent_as_intentional_state()
    {
        var catalogue = Substitute.For<IMediaCatalogue>();
        catalogue.ResolveExerciseMediaAsync("Bodyweight Squat", TestContext.Current.CancellationToken)
            .Returns(ExerciseMediaDescriptor.Absent("Bodyweight Squat", "Use the text guide."));

        var resolved = await catalogue.ResolveExerciseMediaAsync("Bodyweight Squat", TestContext.Current.CancellationToken);

        resolved.Availability.ShouldBe(ExerciseMediaAvailability.Absent);
        resolved.HasPlayableSource.ShouldBeFalse();
        resolved.TextDescription.ShouldBe("Use the text guide.");
    }

    [Fact]
    public async Task ResolveExerciseMediaAsync_can_report_downloaded_media()
    {
        var catalogue = Substitute.For<IMediaCatalogue>();
        catalogue.ResolveExerciseMediaAsync("Goblet Squat", TestContext.Current.CancellationToken)
            .Returns(ExerciseMediaDescriptor.Downloaded("Goblet Squat", "goblet-squat.mp4", "Cached demo.", 1_200_000));

        var resolved = await catalogue.ResolveExerciseMediaAsync("Goblet Squat", TestContext.Current.CancellationToken);

        resolved.Availability.ShouldBe(ExerciseMediaAvailability.Downloaded);
        resolved.HasPlayableSource.ShouldBeTrue();
        resolved.SizeBytes.ShouldBe(1_200_000);
    }
}
