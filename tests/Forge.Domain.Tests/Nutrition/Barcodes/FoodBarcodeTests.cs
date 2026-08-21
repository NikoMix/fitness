using Forge.Domain.Nutrition.Barcodes;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition.Barcodes;

/// <summary>The remembered barcode-to-food mapping.</summary>
public sealed class FoodBarcodeTests
{
    private static Barcode Parse(string raw) => BarcodeNormaliser.Parse(raw).Barcode
        ?? throw new InvalidOperationException($"Test barcode '{raw}' should be valid.");

    [Fact]
    public void A_new_mapping_stores_the_canonical_key_and_the_scanned_spelling()
    {
        var foodId = Guid.CreateVersion7();

        var mapping = FoodBarcode.ForFood(Parse("04252614"), foodId, BarcodeProvenance.UserCreated);

        mapping.Gtin14.ShouldBe("00042100005264");
        mapping.ScannedValue.ShouldBe("04252614");
        mapping.Symbology.ShouldBe(BarcodeSymbology.UpcE);
        mapping.FoodItemId.ShouldBe(foodId);
        mapping.Provenance.ShouldBe(BarcodeProvenance.UserCreated);
        mapping.TimesScanned.ShouldBe(0);
        mapping.LastScannedUtc.ShouldBeNull();
    }

    [Fact]
    public void Recording_a_scan_updates_the_count_and_the_timestamp()
    {
        var mapping = FoodBarcode.ForFood(Parse("4006381333931"), Guid.CreateVersion7(), BarcodeProvenance.UserCreated);
        var first = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 3, 2, 8, 30, 0, TimeSpan.Zero);

        mapping.RecordScan(first);
        mapping.RecordScan(second);

        mapping.TimesScanned.ShouldBe(2);
        mapping.LastScannedUtc.ShouldBe(second);
    }

    /// <summary>
    /// Phone clocks move backwards after a time-zone change or a restored backup. A stale reading
    /// must not overwrite a newer one, or any ordering built on the timestamp becomes nonsense.
    /// </summary>
    [Fact]
    public void A_scan_timestamp_never_moves_backwards()
    {
        var mapping = FoodBarcode.ForFood(Parse("4006381333931"), Guid.CreateVersion7(), BarcodeProvenance.UserCreated);
        var newest = new DateTimeOffset(2026, 3, 2, 8, 30, 0, TimeSpan.Zero);
        var older = new DateTimeOffset(2026, 1, 1, 8, 30, 0, TimeSpan.Zero);

        mapping.RecordScan(newest);
        mapping.RecordScan(older);

        mapping.LastScannedUtc.ShouldBe(newest);

        // The scan still happened, so it is still counted.
        mapping.TimesScanned.ShouldBe(2);
    }

    [Fact]
    public void A_mapping_needs_a_barcode()
    {
        Should.Throw<ArgumentNullException>(
            () => FoodBarcode.ForFood(null!, Guid.CreateVersion7(), BarcodeProvenance.UserCreated));
    }
}
