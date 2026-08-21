using Forge.Domain.Nutrition.Barcodes;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition.Barcodes;

/// <summary>
/// Resolving a scanned barcode against what this device already remembers.
/// </summary>
/// <remarks>
/// The unknown path matters most. Forge calls no food database, so an unknown code is the normal
/// first outcome for almost every product and has to arrive as an ordinary answer the screen can
/// act on, not as a failure.
/// </remarks>
public sealed class BarcodeMatcherTests
{
    private static Barcode Parse(string raw) => BarcodeNormaliser.Parse(raw).Barcode
        ?? throw new InvalidOperationException($"Test barcode '{raw}' should be valid.");

    private static FoodBarcode Mapping(
        string raw,
        Guid foodItemId,
        BarcodeProvenance provenance = BarcodeProvenance.UserCreated) =>
        FoodBarcode.ForFood(Parse(raw), foodItemId, provenance);

    [Fact]
    public void An_unseen_barcode_is_unknown_rather_than_an_error()
    {
        var lookup = BarcodeMatcher.Match(Parse("4006381333931"), []);

        lookup.Status.ShouldBe(BarcodeLookupStatus.Unknown);
        lookup.IsKnown.ShouldBeFalse();
        lookup.Match.ShouldBeNull();
    }

    [Fact]
    public void A_barcode_none_of_the_candidates_carry_is_unknown()
    {
        var candidates = new[] { Mapping("036000291452", Guid.CreateVersion7()) };

        BarcodeMatcher.Match(Parse("4006381333931"), candidates)
            .Status.ShouldBe(BarcodeLookupStatus.Unknown);
    }

    [Fact]
    public void A_remembered_barcode_resolves_to_its_food()
    {
        var foodId = Guid.CreateVersion7();
        var candidates = new[] { Mapping("4006381333931", foodId) };

        var lookup = BarcodeMatcher.Match(Parse("4006381333931"), candidates);

        lookup.IsKnown.ShouldBeTrue();
        lookup.Match.ShouldNotBeNull().FoodItemId.ShouldBe(foodId);
    }

    /// <summary>
    /// The same product read from two different faces of the packaging must resolve to one food.
    /// </summary>
    [Fact]
    public void Matching_is_on_the_canonical_key_not_the_scanned_digits()
    {
        var foodId = Guid.CreateVersion7();
        var storedAsUpcA = new[] { Mapping("042100005264", foodId) };

        BarcodeMatcher.Match(Parse("04252614"), storedAsUpcA)
            .Match.ShouldNotBeNull().FoodItemId.ShouldBe(foodId);
    }

    [Fact]
    public void A_soft_deleted_mapping_is_treated_as_forgotten()
    {
        var mapping = Mapping("4006381333931", Guid.CreateVersion7());
        mapping.DeletedUtc = DateTimeOffset.UtcNow;

        BarcodeMatcher.Match(Parse("4006381333931"), [mapping])
            .Status.ShouldBe(BarcodeLookupStatus.Unknown);
    }

    /// <summary>
    /// A person who repointed a code at a different food was correcting Forge. A shipped mapping
    /// arriving in a later catalogue must not quietly undo that.
    /// </summary>
    [Fact]
    public void A_user_correction_beats_a_shipped_mapping()
    {
        var shippedFood = Guid.CreateVersion7();
        var correctedFood = Guid.CreateVersion7();

        var shipped = Mapping("4006381333931", shippedFood, BarcodeProvenance.ShippedCatalogue);
        shipped.RecordScan(DateTimeOffset.UtcNow);
        var corrected = Mapping("4006381333931", correctedFood, BarcodeProvenance.UserCreated);

        BarcodeMatcher.Match(Parse("4006381333931"), [shipped, corrected])
            .Match.ShouldNotBeNull().FoodItemId.ShouldBe(correctedFood);
    }

    /// <summary>
    /// Duplicates within one provenance should not exist, but a restored backup can merge two
    /// histories, so the tie-break has to be defined rather than left to row order.
    /// </summary>
    [Fact]
    public void Within_one_provenance_the_most_recently_scanned_mapping_wins()
    {
        var stale = Mapping("4006381333931", Guid.CreateVersion7());
        stale.RecordScan(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var recentFood = Guid.CreateVersion7();
        var recent = Mapping("4006381333931", recentFood);
        recent.RecordScan(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        BarcodeMatcher.Match(Parse("4006381333931"), [stale, recent])
            .Match.ShouldNotBeNull().FoodItemId.ShouldBe(recentFood);
    }

    [Fact]
    public void A_never_scanned_mapping_loses_to_one_that_has_been_used()
    {
        var usedFood = Guid.CreateVersion7();
        var used = Mapping("4006381333931", usedFood);
        used.RecordScan(DateTimeOffset.UtcNow);
        var neverUsed = Mapping("4006381333931", Guid.CreateVersion7());

        BarcodeMatcher.Match(Parse("4006381333931"), [neverUsed, used])
            .Match.ShouldNotBeNull().FoodItemId.ShouldBe(usedFood);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => BarcodeMatcher.Match(null!, []));
        Should.Throw<ArgumentNullException>(() => BarcodeMatcher.Match(Parse("4006381333931"), null!));
    }
}
