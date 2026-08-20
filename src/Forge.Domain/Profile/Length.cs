namespace Forge.Domain.Profile;

/// <summary>
/// A length, stored canonically in centimetres.
/// </summary>
/// <remarks>
/// Height and circumference measurements are entered in both metric and imperial units. Storing
/// centimetres keeps persistence unit-unambiguous while allowing display preference to change
/// without rewriting historical records.
/// </remarks>
public readonly record struct Length : IComparable<Length>
{
    private const decimal CentimetresPerInch = 2.54m;
    private const int InchesPerFoot = 12;

    private Length(decimal centimetres) => Centimetres = centimetres;

    /// <summary>The length in centimetres. This is the stored representation.</summary>
    public decimal Centimetres { get; }

    /// <summary>The length expressed in total inches.</summary>
    public double TotalInches => (double)(Centimetres / CentimetresPerInch);

    /// <summary>A zero length.</summary>
    public static Length Zero => new(0m);

    /// <summary>Creates a length from centimetres.</summary>
    public static Length FromCentimetres(decimal centimetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(centimetres);
        return new Length(centimetres);
    }

    /// <summary>Creates a length from total inches.</summary>
    public static Length FromInches(decimal inches)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inches);
        return new Length(inches * CentimetresPerInch);
    }

    /// <summary>Creates a length from feet and inches.</summary>
    public static Length FromFeetAndInches(int feet, decimal inches)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(feet);
        ArgumentOutOfRangeException.ThrowIfNegative(inches);
        return FromInches((feet * InchesPerFoot) + inches);
    }

    /// <summary>Returns the imperial display components for this length.</summary>
    public (int Feet, decimal Inches) ToFeetAndInches()
    {
        var totalInches = Centimetres / CentimetresPerInch;
        var feet = (int)Math.Floor(totalInches / InchesPerFoot);
        var inches = totalInches - (feet * InchesPerFoot);
        return (feet, inches);
    }

    /// <inheritdoc />
    public int CompareTo(Length other) => Centimetres.CompareTo(other.Centimetres);

    /// <summary>Determines whether one length is less than another.</summary>
    public static bool operator <(Length left, Length right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether one length is greater than another.</summary>
    public static bool operator >(Length left, Length right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether one length is less than or equal to another.</summary>
    public static bool operator <=(Length left, Length right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether one length is greater than or equal to another.</summary>
    public static bool operator >=(Length left, Length right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => $"{Centimetres:0.##} cm";
}
