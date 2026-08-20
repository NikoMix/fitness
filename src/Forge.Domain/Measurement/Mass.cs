namespace Forge.Domain.Measurement;

/// <summary>
/// A mass, stored canonically in kilograms.
/// </summary>
/// <remarks>
/// <para>
/// Weight is the single most error-prone value in a fitness application, because users work in
/// two unit systems and the conversion is lossy in one direction. Modelling it as a value type
/// with one canonical unit removes an entire class of bug: a bare <c>double</c> named
/// <c>weight</c> tells you nothing about whether it holds kilograms or pounds, and the mistake
/// is invisible until someone's training log is 2.2 times wrong.
/// </para>
/// <para>
/// Kilograms are canonical because plate mathematics and barbell standards are metric almost
/// everywhere, and because storing one unit means display preference can change at any time
/// without touching stored data.
/// </para>
/// </remarks>
public readonly record struct Mass : IComparable<Mass>
{
    /// <summary>Exact kilograms per pound, per the international avoirdupois definition.</summary>
    private const decimal KilogramsPerPound = 0.45359237m;

    private Mass(decimal kilograms) => Kilograms = kilograms;

    /// <summary>The mass in kilograms. This is the stored representation.</summary>
    public decimal Kilograms { get; }

    /// <summary>The mass expressed in pounds.</summary>
    public double Pounds => (double)(Kilograms / KilogramsPerPound);

    /// <summary>A mass of zero.</summary>
    public static Mass Zero => new(0m);

    /// <summary>Creates a mass from kilograms.</summary>
    /// <param name="kilograms">A non-negative value.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public static Mass FromKilograms(decimal kilograms)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(kilograms);
        return new Mass(kilograms);
    }

    /// <summary>Creates a mass from pounds.</summary>
    /// <param name="pounds">A non-negative value.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public static Mass FromPounds(decimal pounds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pounds);
        return new Mass(pounds * KilogramsPerPound);
    }

    /// <summary>Adds two masses.</summary>
    public static Mass operator +(Mass left, Mass right) => new(left.Kilograms + right.Kilograms);

    /// <summary>Multiplies a mass by a count, used for set volume.</summary>
    public static Mass operator *(Mass mass, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new Mass(mass.Kilograms * count);
    }

    /// <inheritdoc />
    public int CompareTo(Mass other) => Kilograms.CompareTo(other.Kilograms);

    /// <summary>Determines whether one mass is less than another.</summary>
    public static bool operator <(Mass left, Mass right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether one mass is greater than another.</summary>
    public static bool operator >(Mass left, Mass right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether one mass is less than or equal to another.</summary>
    public static bool operator <=(Mass left, Mass right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether one mass is greater than or equal to another.</summary>
    public static bool operator >=(Mass left, Mass right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => $"{Kilograms:0.##} kg";
}
