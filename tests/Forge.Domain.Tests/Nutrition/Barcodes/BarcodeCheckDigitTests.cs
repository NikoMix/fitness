using Forge.Domain.Nutrition.Barcodes;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition.Barcodes;

/// <summary>
/// Check-digit arithmetic for every supported symbology.
/// </summary>
/// <remarks>
/// The published example codes below are the point of these tests. A check-digit implementation
/// that is subtly wrong still returns a digit, still looks reasonable, and still accepts most
/// transposed input - so testing it only against numbers this codebase generated would prove
/// nothing except that the code agrees with itself.
/// </remarks>
public sealed class BarcodeCheckDigitTests
{
    [Theory]
    [InlineData("400638133393", 1)]  // EAN-13, the standard worked example
    [InlineData("9638507", 4)]       // EAN-8, the standard worked example
    [InlineData("03600029145", 2)]   // UPC-A, the standard worked example
    public void Computes_the_published_check_digit(string payload, int expected)
    {
        BarcodeNormaliser.ComputeCheckDigit(payload).ShouldBe(expected);
    }

    [Theory]
    [InlineData("4006381333931")]
    [InlineData("96385074")]
    [InlineData("036000291452")]
    public void Accepts_a_correct_check_digit(string code)
    {
        BarcodeNormaliser.IsCheckDigitValid(code).ShouldBeTrue();
    }

    [Theory]
    [InlineData("4006381333930")]  // last digit altered
    [InlineData("4006381333932")]
    [InlineData("0360002914520")]  // digits shifted
    [InlineData("96385075")]
    public void Rejects_an_incorrect_check_digit(string code)
    {
        BarcodeNormaliser.IsCheckDigitValid(code).ShouldBeFalse();
    }

    /// <summary>
    /// A single transposition is the mistake the check digit exists to catch, and it is exactly
    /// what a person retyping a smudged barcode does.
    /// </summary>
    [Fact]
    public void Rejects_adjacent_transposed_digits()
    {
        // 4006381333931 with the 6 and 3 at positions four and five swapped.
        BarcodeNormaliser.IsCheckDigitValid("4003681333931").ShouldBeFalse();
    }

    [Fact]
    public void Check_digit_is_a_single_digit_for_every_all_nines_payload()
    {
        // 12 nines: 6 weighted by 3 and 6 by 1 gives 216, so the check digit is 4.
        BarcodeNormaliser.ComputeCheckDigit("999999999999").ShouldBe(4);
    }

    /// <summary>
    /// A payload whose weighted sum is a multiple of ten must produce 0 rather than 10, which is
    /// the classic off-by-one in a hand-written modulo-10 routine.
    /// </summary>
    [Fact]
    public void Check_digit_wraps_to_zero_rather_than_ten()
    {
        BarcodeNormaliser.ComputeCheckDigit("00000000000").ShouldBe(0);

        // Weighted sum is 5 * 3 + 5 * 1 = 20, so the remainder is zero and the digit must be too.
        BarcodeNormaliser.ComputeCheckDigit("00000000055").ShouldBe(0);
    }

    [Fact]
    public void Non_digit_payloads_are_rejected_rather_than_scored()
    {
        Should.Throw<ArgumentException>(() => BarcodeNormaliser.ComputeCheckDigit("40063813339X"));
        Should.Throw<ArgumentException>(() => BarcodeNormaliser.ComputeCheckDigit(string.Empty));
        Should.Throw<ArgumentException>(() => BarcodeNormaliser.IsCheckDigitValid("400638133393X"));
    }

    [Fact]
    public void A_single_character_is_not_a_barcode()
    {
        Should.Throw<ArgumentException>(() => BarcodeNormaliser.IsCheckDigitValid("4"));
    }
}
