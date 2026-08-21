namespace Forge.Domain.Nutrition.Barcodes;

/// <summary>Whether a scanned barcode is already known to this device.</summary>
public enum BarcodeLookupStatus
{
    /// <summary>The barcode resolves to a food already stored on this device.</summary>
    Known,

    /// <summary>
    /// The barcode is valid but has never been seen here.
    /// </summary>
    /// <remarks>
    /// The expected outcome for most first scans, and not an error. Forge ships a small original
    /// food catalogue and calls no external food database, so an unknown code means "tell me what
    /// this is once", not "something went wrong".
    /// </remarks>
    Unknown,
}

/// <summary>The result of resolving a barcode against the device's remembered mappings.</summary>
/// <param name="Status">Whether a mapping was found.</param>
/// <param name="Match">The matched mapping, or <see langword="null"/> when unknown.</param>
public sealed record BarcodeLookup(BarcodeLookupStatus Status, FoodBarcode? Match)
{
    /// <summary>Whether a mapping was found.</summary>
    public bool IsKnown => Status == BarcodeLookupStatus.Known;
}

/// <summary>
/// Resolves a barcode against remembered mappings.
/// </summary>
/// <remarks>
/// Kept pure and separate from storage so the selection rules can be tested without a database.
/// The caller supplies the candidate rows; how they were fetched is not this type's concern.
/// </remarks>
public static class BarcodeMatcher
{
    /// <summary>
    /// Finds the mapping a barcode should resolve to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matching is on <see cref="Barcode.Gtin14"/>, so a UPC-A and the EAN-13 for the same product
    /// resolve together, and a UPC-E resolves to whatever its expanded UPC-A resolves to.
    /// </para>
    /// <para>
    /// A unique index means duplicates should not exist, but the rules still have to be defined
    /// because a restored backup can merge two histories. A user-created mapping wins over a
    /// shipped one, because a person who pointed a code at a different food was correcting Forge
    /// and that correction must not be undone by a catalogue row. Within the same provenance the
    /// most recently scanned mapping wins, as the one the person has actually been using.
    /// </para>
    /// </remarks>
    /// <param name="barcode">The validated barcode being resolved.</param>
    /// <param name="candidates">Mappings to search. Soft-deleted mappings are ignored.</param>
    /// <returns>The match, or an unknown result.</returns>
    public static BarcodeLookup Match(Barcode barcode, IEnumerable<FoodBarcode> candidates)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(candidates);

        var match = candidates
            .Where(candidate => candidate is { IsDeleted: false }
                && string.Equals(candidate.Gtin14, barcode.Gtin14, StringComparison.Ordinal))
            .OrderByDescending(candidate => candidate.Provenance == BarcodeProvenance.UserCreated)
            .ThenByDescending(candidate => candidate.LastScannedUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(candidate => candidate.CreatedUtc)
            .FirstOrDefault();

        return match is null
            ? new BarcodeLookup(BarcodeLookupStatus.Unknown, null)
            : new BarcodeLookup(BarcodeLookupStatus.Known, match);
    }
}
