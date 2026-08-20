namespace Forge.Domain.Nutrition;

/// <summary>
/// A liquid volume, stored canonically in millilitres.
/// </summary>
/// <remarks>
/// Hydration logging crosses metric bottles and US fluid-ounce cups constantly. A dedicated
/// value type makes the stored unit explicit and prevents a bare number from being interpreted
/// as the wrong unit.
/// </remarks>
public readonly record struct Volume : IComparable<Volume>
{
    /// <summary>Exact millilitres per US fluid ounce, derived from 1 fl oz = 1/128 US gallon.</summary>
    private const decimal MillilitresPerFluidOunce = 29.5735295625m;

    private Volume(decimal millilitres) => Millilitres = millilitres;

    /// <summary>The volume in millilitres. This is the stored representation.</summary>
    public decimal Millilitres { get; }

    /// <summary>The volume expressed in US fluid ounces.</summary>
    public double FluidOunces => (double)(Millilitres / MillilitresPerFluidOunce);

    /// <summary>A volume of zero.</summary>
    public static Volume Zero => new(0m);

    /// <summary>Creates a volume from millilitres.</summary>
    /// <param name="millilitres">A non-negative value.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public static Volume FromMillilitres(decimal millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        return new Volume(millilitres);
    }

    /// <summary>Creates a volume from US fluid ounces.</summary>
    /// <param name="fluidOunces">A non-negative value.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public static Volume FromFluidOunces(decimal fluidOunces)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fluidOunces);
        return new Volume(fluidOunces * MillilitresPerFluidOunce);
    }

    /// <summary>Adds two volumes.</summary>
    public static Volume operator +(Volume left, Volume right) => new(left.Millilitres + right.Millilitres);

    /// <summary>Multiplies a volume by a count, used for container presets.</summary>
    public static Volume operator *(Volume volume, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new Volume(volume.Millilitres * count);
    }

    /// <inheritdoc />
    public int CompareTo(Volume other) => Millilitres.CompareTo(other.Millilitres);

    /// <summary>Determines whether one volume is less than another.</summary>
    public static bool operator <(Volume left, Volume right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether one volume is greater than another.</summary>
    public static bool operator >(Volume left, Volume right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether one volume is less than or equal to another.</summary>
    public static bool operator <=(Volume left, Volume right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether one volume is greater than or equal to another.</summary>
    public static bool operator >=(Volume left, Volume right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => $"{Millilitres:0.##} ml";
}
