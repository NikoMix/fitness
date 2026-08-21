namespace Forge.Domain.Nutrition.Barcodes;

/// <summary>
/// The retail barcode symbologies Forge understands.
/// </summary>
/// <remarks>
/// Restricted to the GS1 retail family that appears on packaged food. Codes such as Code 128 or
/// GS1 DataBar exist on shipping cases and coupons rather than on the front of a cereal box, so
/// accepting them would only widen the surface of things that can be mis-parsed without making a
/// single extra food loggable.
/// </remarks>
public enum BarcodeSymbology
{
    /// <summary>Thirteen-digit GTIN-13, the dominant form outside North America.</summary>
    Ean13,

    /// <summary>Eight-digit GTIN-8, used on packaging too small for a full EAN-13.</summary>
    Ean8,

    /// <summary>Twelve-digit GTIN-12, the standard North American retail code.</summary>
    UpcA,

    /// <summary>
    /// Eight-digit zero-suppressed UPC.
    /// </summary>
    /// <remarks>
    /// Never stored as scanned. UPC-E is a compressed spelling of a UPC-A, so it is expanded
    /// before anything else looks at it; otherwise the same physical product would fail to match
    /// itself depending on which face of the packet was scanned.
    /// </remarks>
    UpcE,
}

/// <summary>Why a scanned or typed barcode was not accepted.</summary>
/// <remarks>
/// Modelled as an enum rather than a message string because the reason has to be turned into
/// user-facing English by the UI layer, and because the distinction between "you mistyped a
/// digit" and "that is not a food barcode" changes what Forge offers to do next.
/// </remarks>
public enum BarcodeRejectionReason
{
    /// <summary>The barcode was accepted.</summary>
    None,

    /// <summary>Nothing was supplied once separators were removed.</summary>
    Empty,

    /// <summary>The input contained characters that are not digits.</summary>
    NotAllDigits,

    /// <summary>The digit count matches no supported symbology.</summary>
    UnsupportedLength,

    /// <summary>The trailing check digit does not agree with the payload.</summary>
    CheckDigitMismatch,

    /// <summary>A UPC-E code declared a number system other than 0 or 1.</summary>
    UnsupportedNumberSystem,
}

/// <summary>Where a barcode-to-food mapping came from.</summary>
/// <remarks>
/// <para>
/// Forge resolves barcodes entirely on the device and never calls a food database, so the only
/// mappings that exist are ones Forge shipped and ones the person scanned themselves. Recording
/// which is which is what makes a future catalogue refresh safe: shipped rows may be replaced
/// wholesale, user rows must survive untouched. Without the distinction, the only safe refresh
/// would be no refresh at all.
/// </para>
/// <para>
/// The shipped catalogue carries no barcodes today, so every row in a v1 database is
/// <see cref="UserCreated"/>. That is expected rather than a gap: personal barcode memory is the
/// feature, and the enum exists so shipped mappings can be added later without a migration that
/// has to guess at the provenance of rows already on people's phones.
/// </para>
/// </remarks>
public enum BarcodeProvenance
{
    /// <summary>Shipped with the Forge food catalogue.</summary>
    ShippedCatalogue,

    /// <summary>Created on this device when the person scanned an unknown barcode.</summary>
    UserCreated,
}
