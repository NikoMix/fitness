using Forge.Domain.Measurement;

namespace Forge.Domain.Nutrition;

/// <summary>A labelled way a food can be measured by the user.</summary>
public sealed class ServingDefinition
{
    /// <summary>Display label, for example "1 cup" or "100 g".</summary>
    public required string Name { get; set; }

    /// <summary>How many grams of the food this serving represents.</summary>
    public Mass Mass { get; set; } = Mass.Zero;

    /// <summary>Optional liquid volume when the serving is volume-based.</summary>
    public Volume? Volume { get; set; }
}

/// <summary>A specific serving amount selected by the user.</summary>
/// <param name="ServingName">The serving definition label.</param>
/// <param name="Quantity">The count of servings selected.</param>
/// <param name="GramsPerServing">The gram mass represented by one serving.</param>
public sealed record ServingSnapshot(string ServingName, decimal Quantity, decimal GramsPerServing)
{
    /// <summary>Total grams represented by this snapshot.</summary>
    public decimal TotalGrams => Quantity * GramsPerServing;
}

/// <summary>Converts explicit serving definitions to and from grams.</summary>
/// <remarks>
/// Nutrition arithmetic is performed on grams because food labels expose per-100g values. Named
/// servings are therefore modelled as a gram bridge, not as display text, so conversions such as
/// "1 cup" → "100 g" → "2 servings" round-trip through one canonical quantity.
/// </remarks>
public sealed class ServingConversion
{
    private readonly Dictionary<string, ServingDefinition> definitions;

    /// <summary>Creates a converter from named serving definitions.</summary>
    public ServingConversion(IEnumerable<ServingDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        this.definitions = definitions.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Converts a named serving count to grams.</summary>
    public decimal ToGrams(string servingName, decimal quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servingName);
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        return Get(servingName).Mass.Kilograms * 1000m * quantity;
    }

    /// <summary>Converts grams to a named serving count.</summary>
    public decimal FromGrams(decimal grams, string servingName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(grams);
        ArgumentException.ThrowIfNullOrWhiteSpace(servingName);
        var gramsPerServing = Get(servingName).Mass.Kilograms * 1000m;
        if (gramsPerServing == 0m)
        {
            throw new InvalidOperationException("A serving definition must represent more than zero grams.");
        }

        return grams / gramsPerServing;
    }

    /// <summary>Converts a quantity from one named serving to another.</summary>
    public decimal Convert(decimal quantity, string fromServingName, string toServingName) =>
        FromGrams(ToGrams(fromServingName, quantity), toServingName);

    private ServingDefinition Get(string servingName) => definitions.TryGetValue(servingName, out var definition)
        ? definition
        : throw new KeyNotFoundException($"Unknown serving definition '{servingName}'.");
}
