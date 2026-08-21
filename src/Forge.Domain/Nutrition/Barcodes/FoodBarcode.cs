using Forge.Domain.Common;

namespace Forge.Domain.Nutrition.Barcodes;

/// <summary>
/// A remembered mapping from a barcode to a food.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of Forge's barcode "database". There is no lookup service behind it: the
/// first time a packet is scanned Forge cannot know what it is, the person tells it once, and
/// every scan after that is instant and offline. See
/// <c>docs/adr/0001-local-first-no-backend.md</c>.
/// </para>
/// <para>
/// The mapping is a separate entity rather than a column on <see cref="FoodItem" /> because one
/// food legitimately carries several codes - a multipack, a regional variant and an own-brand
/// relabel are the same porridge - and because a mapping has its own provenance and scan history
/// that has nothing to do with the food's nutrition.
/// </para>
/// </remarks>
public sealed class FoodBarcode : Entity
{
    /// <summary>The zero-padded fourteen-digit GS1 key this mapping is found by.</summary>
    public required string Gtin14 { get; init; }

    /// <summary>The digits as originally scanned, kept so the code can be shown back unchanged.</summary>
    public required string ScannedValue { get; init; }

    /// <summary>The symbology the code was read as.</summary>
    public BarcodeSymbology Symbology { get; init; }

    /// <summary>The food this barcode resolves to.</summary>
    public required Guid FoodItemId { get; set; }

    /// <summary>Navigation to the mapped food.</summary>
    public FoodItem? Food { get; init; }

    /// <summary>Whether Forge shipped this mapping or the person created it.</summary>
    public BarcodeProvenance Provenance { get; init; }

    /// <summary>When this mapping was last matched by a scan, or <see langword="null"/> if never.</summary>
    public DateTimeOffset? LastScannedUtc { get; private set; }

    /// <summary>How many times this mapping has been matched.</summary>
    public int TimesScanned { get; private set; }

    /// <summary>Creates a mapping for a validated barcode.</summary>
    /// <param name="barcode">The validated barcode.</param>
    /// <param name="foodItemId">The food the barcode resolves to.</param>
    /// <param name="provenance">Where the mapping came from.</param>
    /// <returns>A new, never-scanned mapping.</returns>
    public static FoodBarcode ForFood(Barcode barcode, Guid foodItemId, BarcodeProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(barcode);

        return new FoodBarcode
        {
            Gtin14 = barcode.Gtin14,
            ScannedValue = barcode.ScannedValue,
            Symbology = barcode.Symbology,
            FoodItemId = foodItemId,
            Provenance = provenance,
        };
    }

    /// <summary>
    /// Records that this mapping was matched by a scan.
    /// </summary>
    /// <remarks>
    /// <see cref="LastScannedUtc"/> never moves backwards. Phone clocks are adjusted by the user,
    /// by the network and by daylight saving, and a restored backup can carry timestamps from the
    /// future; letting a stale reading overwrite a newer one would corrupt any ordering built on
    /// it. The count is incremented regardless, because the scan did happen.
    /// </remarks>
    /// <param name="scannedUtc">When the scan happened, in UTC.</param>
    public void RecordScan(DateTimeOffset scannedUtc)
    {
        if (scannedUtc > (LastScannedUtc ?? DateTimeOffset.MinValue))
        {
            LastScannedUtc = scannedUtc;
        }

        TimesScanned++;
    }
}
