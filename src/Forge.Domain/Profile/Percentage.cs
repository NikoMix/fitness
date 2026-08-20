namespace Forge.Domain.Profile;

/// <summary>
/// A percentage value constrained to the inclusive range 0-100.
/// </summary>
public readonly record struct Percentage : IComparable<Percentage>
{
    private Percentage(decimal value) => Value = value;

    /// <summary>The percentage value, from 0 to 100.</summary>
    public decimal Value { get; }

    /// <summary>The value as a fraction, from 0 to 1.</summary>
    public decimal Fraction => Value / 100m;

    /// <summary>Zero percent.</summary>
    public static Percentage Zero => new(0m);

    /// <summary>Creates a percentage from a 0-100 value.</summary>
    public static Percentage FromValue(decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (value > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Percentage cannot exceed 100.");
        }

        return new Percentage(value);
    }

    /// <summary>Creates a percentage from a 0-1 fraction.</summary>
    public static Percentage FromFraction(decimal fraction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fraction);
        if (fraction > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "Fraction cannot exceed 1.");
        }

        return new Percentage(fraction * 100m);
    }

    /// <inheritdoc />
    public int CompareTo(Percentage other) => Value.CompareTo(other.Value);

    /// <summary>Determines whether one percentage is less than another.</summary>
    public static bool operator <(Percentage left, Percentage right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether one percentage is greater than another.</summary>
    public static bool operator >(Percentage left, Percentage right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether one percentage is less than or equal to another.</summary>
    public static bool operator <=(Percentage left, Percentage right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether one percentage is greater than or equal to another.</summary>
    public static bool operator >=(Percentage left, Percentage right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => $"{Value:0.##}%";
}
