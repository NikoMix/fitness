using Forge.Core.Abstractions.Media;
using Forge.Infrastructure.Media;
using NSubstitute;
using Shouldly;

namespace Forge.Infrastructure.Tests.Media;

/// <summary>
/// Pins the exercise video resolver to the store that actually downloads packs.
/// </summary>
/// <remarks>
/// The defect these guard against was not a wrong answer, it was two stores: the resolver read a
/// local HTTP cache nothing ever wrote to while the library downloaded Play Asset Delivery packs,
/// so a demonstration could never appear no matter what the user downloaded. Every test here goes
/// through <see cref="IMediaPackService"/> for that reason.
/// </remarks>
public sealed class ExerciseMediaCatalogueTests : IDisposable
{
    private const string Exercise = "Bodyweight Squat";
    private const string AssetName = "bodyweight-squat.mp4";

    private readonly string temporaryDirectory =
        Path.Combine(Path.GetTempPath(), "forge-media-tests", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task A_platform_without_asset_delivery_reports_absent_rather_than_failing()
    {
        var packs = Substitute.For<IMediaPackService>();
        packs.IsSupported.Returns(false);

        var media = await new ExerciseMediaCatalogue(packs)
            .ResolveExerciseMediaAsync(Exercise, TestContext.Current.CancellationToken);

        media.Availability.ShouldBe(ExerciseMediaAvailability.Absent);
        media.HasPlayableSource.ShouldBeFalse();
        media.TextDescription.ShouldNotBeNullOrWhiteSpace();
        await packs.DidNotReceive().GetPacksAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_downloaded_pack_holding_the_file_produces_a_playable_source()
    {
        var path = WriteAsset(AssetName, 2048);
        var packs = ReadyPack(MediaQuality.High, path);

        var media = await new ExerciseMediaCatalogue(packs)
            .ResolveExerciseMediaAsync(Exercise, TestContext.Current.CancellationToken);

        media.Availability.ShouldBe(ExerciseMediaAvailability.Downloaded);
        media.HasPlayableSource.ShouldBeTrue();
        media.Source.ShouldBe(path);
        media.SizeBytes.ShouldBe(2048);
    }

    [Fact]
    public async Task The_file_name_asked_for_is_the_one_the_packs_are_published_under()
    {
        var packs = ReadyPack(MediaQuality.High, WriteAsset(AssetName, 16));

        await new ExerciseMediaCatalogue(packs)
            .ResolveExerciseMediaAsync(Exercise, TestContext.Current.CancellationToken);

        await packs.Received().GetAssetPathAsync("pack-high", AssetName, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_downloaded_says_so_and_does_not_claim_the_exercise_has_no_video()
    {
        var packs = Substitute.For<IMediaPackService>();
        packs.IsSupported.Returns(true);
        packs.GetPacksAsync(Arg.Any<CancellationToken>()).Returns(Published(Pack("pack-high", MediaQuality.High)));
        packs.GetStatusAsync("pack-high", Arg.Any<CancellationToken>())
            .Returns(new MediaPackStatus("pack-high", MediaPackState.NotDownloaded, 0, 0));

        var media = await new ExerciseMediaCatalogue(packs)
            .ResolveExerciseMediaAsync(Exercise, TestContext.Current.CancellationToken);

        media.Availability.ShouldBe(ExerciseMediaAvailability.Absent);
        media.TextDescription.ShouldNotBeNull();
        media.TextDescription.ShouldContain("video library");
        await packs.DidNotReceive().GetAssetPathAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_downloaded_pack_that_omits_the_exercise_says_that_rather_than_asking_for_a_download()
    {
        var packs = ReadyPack(MediaQuality.High, assetPath: null);

        var media = await new ExerciseMediaCatalogue(packs)
            .ResolveExerciseMediaAsync(Exercise, TestContext.Current.CancellationToken);

        media.Availability.ShouldBe(ExerciseMediaAvailability.Absent);
        media.TextDescription.ShouldNotBeNull();
        media.TextDescription.ShouldContain("does not include");
    }

    [Fact]
    public async Task The_highest_fidelity_downloaded_pack_wins()
    {
        var standardPath = WriteAsset("standard-" + AssetName, 8);
        var maxPath = WriteAsset("max-" + AssetName, 64);

        var packs = Substitute.For<IMediaPackService>();
        packs.IsSupported.Returns(true);
        packs.GetPacksAsync(Arg.Any<CancellationToken>()).Returns(
            Published(Pack("pack-standard", MediaQuality.Standard), Pack("pack-max", MediaQuality.Max)));
        packs.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new MediaPackStatus(callInfo.Arg<string>(), MediaPackState.Ready, 1, 1));
        packs.GetAssetPathAsync("pack-standard", AssetName, Arg.Any<CancellationToken>()).Returns(standardPath);
        packs.GetAssetPathAsync("pack-max", AssetName, Arg.Any<CancellationToken>()).Returns(maxPath);

        var media = await new ExerciseMediaCatalogue(packs)
            .ResolveExerciseMediaAsync(Exercise, TestContext.Current.CancellationToken);

        media.Source.ShouldBe(maxPath);
    }

    [Fact]
    public async Task A_store_failure_costs_the_video_and_never_reaches_the_screen_as_an_error()
    {
        var packs = Substitute.For<IMediaPackService>();
        packs.IsSupported.Returns(true);
        packs.GetPacksAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<MediaPack>>(_ =>
                throw new InvalidOperationException("Store internals a user must never read."));

        var media = await new ExerciseMediaCatalogue(packs)
            .ResolveExerciseMediaAsync(Exercise, TestContext.Current.CancellationToken);

        media.Availability.ShouldBe(ExerciseMediaAvailability.Absent);
        media.TextDescription.ShouldNotBeNull();
        media.TextDescription.ShouldNotContain("Store internals");
    }

    [Fact]
    public async Task A_published_but_empty_catalogue_of_packs_is_not_treated_as_a_missing_download()
    {
        var packs = Substitute.For<IMediaPackService>();
        packs.IsSupported.Returns(true);
        packs.GetPacksAsync(Arg.Any<CancellationToken>()).Returns(Published());

        var media = await new ExerciseMediaCatalogue(packs)
            .ResolveExerciseMediaAsync(Exercise, TestContext.Current.CancellationToken);

        media.Availability.ShouldBe(ExerciseMediaAvailability.Absent);
        media.TextDescription.ShouldNotBeNull();
        media.TextDescription.ShouldContain("published");
    }

    private static IMediaPackService ReadyPack(MediaQuality quality, string? assetPath)
    {
        var packId = "pack-" + quality.ToString().ToLowerInvariant();
        var packs = Substitute.For<IMediaPackService>();
        packs.IsSupported.Returns(true);
        packs.GetPacksAsync(Arg.Any<CancellationToken>()).Returns(Published(Pack(packId, quality)));
        packs.GetStatusAsync(packId, Arg.Any<CancellationToken>())
            .Returns(new MediaPackStatus(packId, MediaPackState.Ready, 1, 1));
        packs.GetAssetPathAsync(packId, AssetName, Arg.Any<CancellationToken>()).Returns(assetPath);
        return packs;
    }

    private static MediaPack[] Published(params MediaPack[] packs) => packs;

    private static MediaPack Pack(string id, MediaQuality quality) =>
        new(id, $"Exercise videos - {quality}", quality, 1_000, ["Squat"]);

    private string WriteAsset(string fileName, int bytes)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, fileName);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }
}
