namespace Forge.Domain.Nutrition.Barcodes;

/// <summary>
/// A validated retail food barcode.
/// </summary>
/// <remarks>
/// <para>
/// Instances only exist for input that passed check-digit validation, so anything holding a
/// <see cref="Barcode"/> can stop re-checking it. Construction therefore goes through
/// <see cref="BarcodeNormaliser.Parse(string?, BarcodeSymbology?)"/> rather than a public
/// constructor.
/// </para>
/// <para>
/// Three spellings of the same code are kept deliberately. <see cref="ScannedValue"/> is what the
/// camera or the person actually produced and is what gets shown back to them, because a UPC-E
/// scanner user who is told their barcode is twelve digits long will reasonably assume Forge read
/// the wrong packet. <see cref="Value"/> is the symbology's canonical spelling with UPC-E expanded.
/// <see cref="Gtin14"/> is the matching key, and it is the only one lookups use.
/// </para>
/// </remarks>
public sealed record Barcode
{
    internal Barcode(string value, string scannedValue, BarcodeSymbology symbology)
    {
        Value = value;
        ScannedValue = scannedValue;
        Symbology = symbology;
        Gtin14 = value.PadLeft(14, '0');
    }

    /// <summary>The canonical digits for the symbology, with UPC-E expanded to UPC-A.</summary>
    public string Value { get; }

    /// <summary>The digits as scanned or typed, with separators removed but nothing expanded.</summary>
    public string ScannedValue { get; }

    /// <summary>The symbology the code was read as.</summary>
    public BarcodeSymbology Symbology { get; }

    /// <summary>
    /// The zero-padded fourteen-digit GS1 key used for all matching.
    /// </summary>
    /// <remarks>
    /// GS1 defines shorter GTINs as right-aligned within a fourteen-digit field, which is what
    /// makes a UPC-A and the EAN-13 printed on the same product's export packaging resolve to one
    /// row. Storing the code at its scanned length instead would silently split a product into two
    /// entries the first time someone bought the imported version.
    /// </remarks>
    public string Gtin14 { get; }

    /// <summary>Returns the canonical digits.</summary>
    /// <returns>The value of <see cref="Value"/>.</returns>
    public override string ToString() => Value;
}

/// <summary>
/// The outcome of interpreting raw barcode input.
/// </summary>
/// <remarks>
/// A rejected barcode is an ordinary result rather than an exception: on a scanner page it happens
/// constantly - a partial read, a hand moving, a mistyped digit - and control flow built on
/// exceptions would make the common case the expensive one.
/// </remarks>
public sealed record BarcodeParseResult
{
    private BarcodeParseResult(Barcode? barcode, BarcodeRejectionReason reason)
    {
        Barcode = barcode;
        Reason = reason;
    }

    /// <summary>The parsed barcode, or <see langword="null"/> when the input was rejected.</summary>
    public Barcode? Barcode { get; }

    /// <summary>Why the input was rejected, or <see cref="BarcodeRejectionReason.None"/>.</summary>
    public BarcodeRejectionReason Reason { get; }

    /// <summary>Whether the input produced a usable barcode.</summary>
    public bool IsValid => Barcode is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="barcode">The validated barcode.</param>
    /// <returns>A result carrying <paramref name="barcode"/>.</returns>
    public static BarcodeParseResult Accepted(Barcode barcode)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        return new BarcodeParseResult(barcode, BarcodeRejectionReason.None);
    }

    /// <summary>Creates a rejected result.</summary>
    /// <param name="reason">Why the input was rejected.</param>
    /// <returns>A result carrying <paramref name="reason"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="reason"/> is <see cref="BarcodeRejectionReason.None"/>, which would describe
    /// a rejection that did not happen.
    /// </exception>
    public static BarcodeParseResult Rejected(BarcodeRejectionReason reason)
    {
        if (reason == BarcodeRejectionReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A rejection must state a reason.");
        }

        return new BarcodeParseResult(null, reason);
    }
}
