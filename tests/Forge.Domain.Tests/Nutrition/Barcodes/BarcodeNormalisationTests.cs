using Forge.Domain.Nutrition.Barcodes;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition.Barcodes;

/// <summary>Normalisation and symbology selection for scanned or typed barcodes.</summary>
public sealed class BarcodeNormalisationTests
{
    [Theory]
    [InlineData("4006381333931", "4006381333931")]
    [InlineData(" 4006381333931 ", "4006381333931")]
    [InlineData("4006-381-333931", "4006381333931")]
    [InlineData("4006 3813 3393 1", "4006381333931")]
    [InlineData("4006\u00a0381333931", "4006381333931")]   // non-breaking space, common in pasted text
    [InlineData("4006\u2013381333931", "4006381333931")]   // en dash, common from a word processor
    [InlineData("4006\t381333931\r\n", "4006381333931")]
    public void Strips_whitespace_and_dashes(string raw, string expected)
    {
        BarcodeNormaliser.StripSeparators(raw).ShouldBe(expected);
    }

    /// <summary>
    /// Only separators are removed. Deleting anything else would let a typo validate as a
    /// different, real barcode instead of being reported back to the person who made it.
    /// </summary>
    [Theory]
    [InlineData("400638133393X", "400638133393X")]
    [InlineData("4006.381333931", "4006.381333931")]
    [InlineData("#4006381333931", "#4006381333931")]
    public void Leaves_everything_that_is_not_a_separator(string raw, string expected)
    {
        BarcodeNormaliser.StripSeparators(raw).ShouldBe(expected);
    }

    [Fact]
    public void Stripping_null_or_empty_yields_an_empty_string()
    {
        BarcodeNormaliser.StripSeparators(null).ShouldBe(string.Empty);
        BarcodeNormaliser.StripSeparators(string.Empty).ShouldBe(string.Empty);
        BarcodeNormaliser.StripSeparators("  - ").ShouldBe(string.Empty);
    }

    [Theory]
    [InlineData("4006381333931", BarcodeSymbology.Ean13)]
    [InlineData("036000291452", BarcodeSymbology.UpcA)]
    [InlineData("96385074", BarcodeSymbology.Ean8)]
    [InlineData("04252614", BarcodeSymbology.UpcE)]
    public void Infers_the_symbology_from_the_digits(string raw, BarcodeSymbology expected)
    {
        BarcodeNormaliser.Parse(raw).Barcode.ShouldNotBeNull().Symbology.ShouldBe(expected);
    }

    [Fact]
    public void Parses_a_barcode_that_arrived_with_separators()
    {
        var result = BarcodeNormaliser.Parse(" 4006-3813 3393 1 ");

        result.IsValid.ShouldBeTrue();
        result.Barcode!.Value.ShouldBe("4006381333931");
        result.Barcode.ScannedValue.ShouldBe("4006381333931");
    }

    /// <summary>
    /// GS1 defines shorter codes as right-aligned in a fourteen-digit field, so the UPC-A on a
    /// domestic pack and the EAN-13 on the imported one resolve to the same stored mapping.
    /// </summary>
    [Fact]
    public void Upc_a_and_its_ean_13_spelling_produce_the_same_matching_key()
    {
        var upcA = BarcodeNormaliser.Parse("036000291452").Barcode.ShouldNotBeNull();
        var ean13 = BarcodeNormaliser.Parse("0036000291452").Barcode.ShouldNotBeNull();

        upcA.Gtin14.ShouldBe("00036000291452");
        ean13.Gtin14.ShouldBe(upcA.Gtin14);

        // The scanned spelling is still preserved separately, so neither is shown back wrongly.
        upcA.Value.ShouldBe("036000291452");
        ean13.Value.ShouldBe("0036000291452");
    }

    [Theory]
    [InlineData(null, BarcodeRejectionReason.Empty)]
    [InlineData("", BarcodeRejectionReason.Empty)]
    [InlineData("    ", BarcodeRejectionReason.Empty)]
    [InlineData("---", BarcodeRejectionReason.Empty)]
    [InlineData("400638133393X", BarcodeRejectionReason.NotAllDigits)]
    [InlineData("40063813 3393O", BarcodeRejectionReason.NotAllDigits)]
    [InlineData("12345", BarcodeRejectionReason.UnsupportedLength)]
    [InlineData("1234567890", BarcodeRejectionReason.UnsupportedLength)]
    [InlineData("12345678901234", BarcodeRejectionReason.UnsupportedLength)]
    [InlineData("4006381333930", BarcodeRejectionReason.CheckDigitMismatch)]
    [InlineData("036000291451", BarcodeRejectionReason.CheckDigitMismatch)]
    [InlineData("96385075", BarcodeRejectionReason.CheckDigitMismatch)]
    public void Rejects_input_with_the_reason_the_screen_needs(string? raw, BarcodeRejectionReason expected)
    {
        var result = BarcodeNormaliser.Parse(raw);

        result.IsValid.ShouldBeFalse();
        result.Barcode.ShouldBeNull();
        result.Reason.ShouldBe(expected);
    }

    /// <summary>
    /// Eight digits are genuinely ambiguous: 01234565 satisfies both the EAN-8 check digit and the
    /// UPC-E one. Without a scanner to say which it read, the number system rules decide.
    /// </summary>
    [Fact]
    public void Ambiguous_eight_digit_codes_are_read_as_upc_e()
    {
        BarcodeNormaliser.IsCheckDigitValid("01234565").ShouldBeTrue();

        var inferred = BarcodeNormaliser.Parse("01234565").Barcode.ShouldNotBeNull();

        inferred.Symbology.ShouldBe(BarcodeSymbology.UpcE);
        inferred.Value.ShouldBe("012345000065");
    }

    /// <summary>A scanner that reports its symbology is believed over the inference rules.</summary>
    [Fact]
    public void A_scanner_reported_symbology_overrides_inference()
    {
        var hinted = BarcodeNormaliser.Parse("01234565", BarcodeSymbology.Ean8).Barcode.ShouldNotBeNull();

        hinted.Symbology.ShouldBe(BarcodeSymbology.Ean8);
        hinted.Value.ShouldBe("01234565");
        hinted.Gtin14.ShouldBe("00000001234565");
    }

    /// <summary>
    /// A hint that disagrees with the digit count is a rejection. Reinterpreting it would mean
    /// trusting a decoder that has already contradicted itself.
    /// </summary>
    [Theory]
    [InlineData("4006381333931", BarcodeSymbology.UpcA)]
    [InlineData("036000291452", BarcodeSymbology.Ean13)]
    [InlineData("96385074", BarcodeSymbology.Ean13)]
    public void A_hint_that_contradicts_the_length_is_rejected(string raw, BarcodeSymbology hint)
    {
        BarcodeNormaliser.Parse(raw, hint).Reason.ShouldBe(BarcodeRejectionReason.UnsupportedLength);
    }

    [Fact]
    public void TryParse_mirrors_Parse()
    {
        BarcodeNormaliser.TryParse("4006381333931", out var accepted).ShouldBeTrue();
        accepted.ShouldNotBeNull().Value.ShouldBe("4006381333931");

        BarcodeNormaliser.TryParse("4006381333930", out var rejected).ShouldBeFalse();
        rejected.ShouldBeNull();
    }

    [Fact]
    public void Barcodes_with_the_same_digits_are_equal()
    {
        var first = BarcodeNormaliser.Parse("4006381333931").Barcode;
        var second = BarcodeNormaliser.Parse("4006-381-333931").Barcode;

        first.ShouldBe(second);
        first!.ToString().ShouldBe("4006381333931");
    }
}
