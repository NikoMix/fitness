using Forge.Domain.Nutrition.Barcodes;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition.Barcodes;

/// <summary>
/// UPC-E expansion.
/// </summary>
/// <remarks>
/// Every branch of the zero-reinsertion table is exercised, because the branches differ only in
/// where the zeros land and a wrong branch produces a twelve-digit number that still passes a
/// length check and still looks like a barcode. Each expansion is asserted to satisfy the UPC-A
/// check digit as well, which is a genuinely independent check: the compressed and expanded forms
/// share a check digit, so an expansion that shuffles digits incorrectly will not validate.
/// </remarks>
public sealed class UpcExpansionTests
{
    [Theory]
    [InlineData("04252614", "042100005264")]  // last data digit 1: manufacturer of three
    [InlineData("04252623", "042200005263")]  // last data digit 2
    [InlineData("05678935", "056700000895")]  // last data digit 3
    [InlineData("01234543", "012340000053")]  // last data digit 4
    [InlineData("01234565", "012345000065")]  // last data digit 5 to 9
    [InlineData("12345670", "123456000070")]  // number system 1
    public void Expands_to_the_matching_upc_a(string upcE, string expectedUpcA)
    {
        var expanded = BarcodeNormaliser.ExpandUpcEToUpcA(upcE);

        expanded.ShouldBe(expectedUpcA);
        expanded.Length.ShouldBe(12);
        BarcodeNormaliser.IsCheckDigitValid(expanded).ShouldBeTrue();
    }

    /// <summary>The compressed form carries the expanded form's check digit unchanged.</summary>
    [Theory]
    [InlineData("04252614")]
    [InlineData("01234565")]
    [InlineData("12345670")]
    public void Carries_the_check_digit_across_unchanged(string upcE)
    {
        BarcodeNormaliser.ExpandUpcEToUpcA(upcE)[^1].ShouldBe(upcE[^1]);
    }

    [Fact]
    public void Parsing_a_upc_e_stores_the_expanded_value_and_the_scanned_one()
    {
        var result = BarcodeNormaliser.Parse("04252614");

        result.IsValid.ShouldBeTrue();
        result.Barcode!.Symbology.ShouldBe(BarcodeSymbology.UpcE);
        result.Barcode.Value.ShouldBe("042100005264");
        result.Barcode.ScannedValue.ShouldBe("04252614");
        result.Barcode.Gtin14.ShouldBe("00042100005264");
    }

    /// <summary>
    /// A UPC-E and the UPC-A printed on the larger pack of the same product must reach one row,
    /// otherwise buying the big box would silently create a second food.
    /// </summary>
    [Fact]
    public void Compressed_and_expanded_forms_share_a_matching_key()
    {
        var compressed = BarcodeNormaliser.Parse("04252614").Barcode.ShouldNotBeNull();
        var full = BarcodeNormaliser.Parse("042100005264").Barcode.ShouldNotBeNull();

        compressed.Gtin14.ShouldBe(full.Gtin14);
        compressed.Symbology.ShouldBe(BarcodeSymbology.UpcE);
        full.Symbology.ShouldBe(BarcodeSymbology.UpcA);
    }

    [Fact]
    public void Rejects_a_upc_e_whose_expansion_fails_its_check_digit()
    {
        // 04252614 with the check digit altered.
        BarcodeNormaliser.Parse("04252615").Reason.ShouldBe(BarcodeRejectionReason.CheckDigitMismatch);
    }

    [Fact]
    public void Rejects_a_number_system_upc_e_does_not_define()
    {
        // Eight digits starting 9 are read as EAN-8 unless the scanner insists on UPC-E, and
        // UPC-E has no number system 9 to expand.
        BarcodeNormaliser.Parse("96385074", BarcodeSymbology.UpcE)
            .Reason.ShouldBe(BarcodeRejectionReason.UnsupportedNumberSystem);
    }

    [Theory]
    [InlineData("0123456")]     // seven digits
    [InlineData("012345650")]   // nine digits
    [InlineData("0123456X")]
    [InlineData("96385074")]    // number system 9
    public void Direct_expansion_rejects_input_that_is_not_a_upc_e(string value)
    {
        Should.Throw<ArgumentException>(() => BarcodeNormaliser.ExpandUpcEToUpcA(value));
    }
}
