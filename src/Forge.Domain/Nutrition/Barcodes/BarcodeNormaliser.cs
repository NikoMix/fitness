using System.Globalization;

namespace Forge.Domain.Nutrition.Barcodes;

/// <summary>
/// Turns raw barcode text into a validated <see cref="Barcode"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately pure and lives in the domain rather than beside the camera code. A
/// check-digit routine that is subtly wrong does not fail loudly - it quietly accepts transposed
/// digits and writes a mapping to the wrong food, which then looks like Forge inventing nutrition
/// data. Keeping the arithmetic here is what makes it cheap to test exhaustively without a device.
/// </para>
/// <para>
/// Every supported symbology uses the same GS1 modulo-10 check digit, so one routine covers all
/// four. The weights alternate 3 and 1 from the right of the payload, which is why EAN-13 appears
/// to weight even positions by 3 while EAN-8 weights odd ones: the payloads have different
/// parities, not different algorithms.
/// </para>
/// </remarks>
public static class BarcodeNormaliser
{
    /// <summary>
    /// Removes the separators people and label printers put inside barcodes.
    /// </summary>
    /// <remarks>
    /// Only whitespace and dashes are removed. Anything else is left in place so that
    /// <see cref="Parse(string?, BarcodeSymbology?)"/> can reject it as not-a-barcode rather than
    /// silently deleting a character and validating a code the person never typed.
    /// </remarks>
    /// <param name="raw">Raw scanned or typed text. May be <see langword="null"/>.</param>
    /// <returns>The input with whitespace and dashes removed.</returns>
    public static string StripSeparators(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var buffer = new char[raw.Length];
        var length = 0;

        foreach (var character in raw)
        {
            if (IsSeparator(character))
            {
                continue;
            }

            buffer[length++] = character;
        }

        return new string(buffer, 0, length);
    }

    /// <summary>
    /// Computes the GS1 modulo-10 check digit for a barcode payload.
    /// </summary>
    /// <param name="payload">The barcode digits excluding the trailing check digit.</param>
    /// <returns>The check digit, 0 to 9.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="payload"/> is empty or contains a character that is not a digit.
    /// </exception>
    public static int ComputeCheckDigit(string payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(payload);

        var sum = 0;
        for (var offset = 0; offset < payload.Length; offset++)
        {
            var character = payload[payload.Length - 1 - offset];
            if (!char.IsAsciiDigit(character))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "Barcode payload '{0}' contains a non-digit character.", payload),
                    nameof(payload));
            }

            var digit = character - '0';

            // Weights alternate 3, 1, 3, 1 ... counting from the digit nearest the check digit.
            sum += offset % 2 == 0 ? digit * 3 : digit;
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>
    /// Checks a complete barcode against its own trailing check digit.
    /// </summary>
    /// <param name="code">The full barcode including its check digit.</param>
    /// <returns><see langword="true"/> when the check digit agrees with the payload.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> is shorter than two characters or contains a non-digit character.
    /// </exception>
    public static bool IsCheckDigitValid(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        if (code.Length < 2)
        {
            throw new ArgumentException("A barcode needs a payload and a check digit.", nameof(code));
        }

        var declared = code[^1];
        if (!char.IsAsciiDigit(declared))
        {
            throw new ArgumentException(
                string.Format(CultureInfo.InvariantCulture, "Barcode '{0}' contains a non-digit character.", code),
                nameof(code));
        }

        return ComputeCheckDigit(code[..^1]) == declared - '0';
    }

    /// <summary>
    /// Expands a zero-suppressed UPC-E code to its full UPC-A form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The last data digit selects where the suppressed zeros are reinserted. The check digit is
    /// carried across unchanged because a UPC-E check digit is defined as the check digit of the
    /// UPC-A it expands to - recomputing it from the compressed digits would produce a different,
    /// wrong answer.
    /// </para>
    /// <para>
    /// Only number systems 0 and 1 are defined for UPC-E, which is also what disambiguates an
    /// eight-digit UPC-E from an eight-digit EAN-8 in <see cref="Parse(string?, BarcodeSymbology?)"/>.
    /// </para>
    /// </remarks>
    /// <param name="upcE">An eight-digit UPC-E code including its check digit.</param>
    /// <returns>The twelve-digit UPC-A equivalent.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="upcE"/> is not eight digits, or declares a number system other than 0 or 1.
    /// </exception>
    public static string ExpandUpcEToUpcA(string upcE)
    {
        ArgumentException.ThrowIfNullOrEmpty(upcE);

        if (upcE.Length != UpcELength)
        {
            throw new ArgumentException("A UPC-E code is eight digits long.", nameof(upcE));
        }

        foreach (var character in upcE)
        {
            if (!char.IsAsciiDigit(character))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "UPC-E code '{0}' contains a non-digit character.", upcE),
                    nameof(upcE));
            }
        }

        if (upcE[0] is not ('0' or '1'))
        {
            throw new ArgumentException("UPC-E defines only number systems 0 and 1.", nameof(upcE));
        }

        var numberSystem = upcE[0];
        var d1 = upcE[1];
        var d2 = upcE[2];
        var d3 = upcE[3];
        var d4 = upcE[4];
        var d5 = upcE[5];
        var d6 = upcE[6];
        var check = upcE[7];

        Span<char> upcA = stackalloc char[UpcALength];
        upcA.Fill('0');
        upcA[0] = numberSystem;
        upcA[^1] = check;

        switch (d6)
        {
            case '0':
            case '1':
            case '2':
                upcA[1] = d1;
                upcA[2] = d2;
                upcA[3] = d6;
                upcA[8] = d3;
                upcA[9] = d4;
                upcA[10] = d5;
                break;

            case '3':
                upcA[1] = d1;
                upcA[2] = d2;
                upcA[3] = d3;
                upcA[9] = d4;
                upcA[10] = d5;
                break;

            case '4':
                upcA[1] = d1;
                upcA[2] = d2;
                upcA[3] = d3;
                upcA[4] = d4;
                upcA[10] = d5;
                break;

            default:
                upcA[1] = d1;
                upcA[2] = d2;
                upcA[3] = d3;
                upcA[4] = d4;
                upcA[5] = d5;
                upcA[10] = d6;
                break;
        }

        return new string(upcA);
    }

    /// <summary>
    /// Interprets raw barcode text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Eight digits are genuinely ambiguous between EAN-8 and UPC-E, and a code can occasionally be
    /// valid as both. When a scanner reports which symbology it read, pass it as
    /// <paramref name="symbologyHint"/> and that answer is used. Without a hint, eight digits
    /// starting 0 or 1 are read as UPC-E because those are the only UPC-E number systems and GS1
    /// does not issue EAN-8 codes in those ranges for open retail circulation.
    /// </para>
    /// <para>
    /// A hint is honoured only for a code of the right length; a hint that disagrees with the digit
    /// count is a rejection rather than something to silently reinterpret.
    /// </para>
    /// </remarks>
    /// <param name="raw">Raw scanned or typed text. May be <see langword="null"/>.</param>
    /// <param name="symbologyHint">The symbology a scanner reported, when it reported one.</param>
    /// <returns>The parsed barcode, or the reason it was rejected.</returns>
    public static BarcodeParseResult Parse(string? raw, BarcodeSymbology? symbologyHint = null)
    {
        var digits = StripSeparators(raw);

        if (digits.Length == 0)
        {
            return BarcodeParseResult.Rejected(BarcodeRejectionReason.Empty);
        }

        foreach (var character in digits)
        {
            if (!char.IsAsciiDigit(character))
            {
                return BarcodeParseResult.Rejected(BarcodeRejectionReason.NotAllDigits);
            }
        }

        var symbology = symbologyHint ?? InferSymbology(digits);
        if (symbology is null || digits.Length != ExpectedLength(symbology.Value))
        {
            return BarcodeParseResult.Rejected(BarcodeRejectionReason.UnsupportedLength);
        }

        if (symbology.Value != BarcodeSymbology.UpcE)
        {
            return IsCheckDigitValid(digits)
                ? BarcodeParseResult.Accepted(new Barcode(digits, digits, symbology.Value))
                : BarcodeParseResult.Rejected(BarcodeRejectionReason.CheckDigitMismatch);
        }

        if (digits[0] is not ('0' or '1'))
        {
            return BarcodeParseResult.Rejected(BarcodeRejectionReason.UnsupportedNumberSystem);
        }

        var expanded = ExpandUpcEToUpcA(digits);
        return IsCheckDigitValid(expanded)
            ? BarcodeParseResult.Accepted(new Barcode(expanded, digits, BarcodeSymbology.UpcE))
            : BarcodeParseResult.Rejected(BarcodeRejectionReason.CheckDigitMismatch);
    }

    /// <summary>Interprets raw barcode text, discarding the rejection reason.</summary>
    /// <param name="raw">Raw scanned or typed text. May be <see langword="null"/>.</param>
    /// <param name="barcode">The parsed barcode, or <see langword="null"/> when rejected.</param>
    /// <returns><see langword="true"/> when the input produced a usable barcode.</returns>
    public static bool TryParse(string? raw, out Barcode? barcode)
    {
        var result = Parse(raw);
        barcode = result.Barcode;
        return result.IsValid;
    }

    private const int UpcELength = 8;
    private const int UpcALength = 12;

    private static BarcodeSymbology? InferSymbology(string digits) => digits.Length switch
    {
        8 => digits[0] is '0' or '1' ? BarcodeSymbology.UpcE : BarcodeSymbology.Ean8,
        12 => BarcodeSymbology.UpcA,
        13 => BarcodeSymbology.Ean13,
        _ => null,
    };

    private static int ExpectedLength(BarcodeSymbology symbology) => symbology switch
    {
        BarcodeSymbology.Ean13 => 13,
        BarcodeSymbology.UpcA => 12,
        BarcodeSymbology.Ean8 => UpcELength,
        BarcodeSymbology.UpcE => UpcELength,
        _ => 0,
    };

    private static bool IsSeparator(char character) =>
        char.IsWhiteSpace(character)
        || char.GetUnicodeCategory(character) == UnicodeCategory.DashPunctuation
        || character == '\u2212';
}
