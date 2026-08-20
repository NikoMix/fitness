namespace Forge.Domain.Nutrition;

/// <summary>Nutrition values for a known mass of food.</summary>
/// <param name="EnergyKilocalories">Energy in kilocalories.</param>
/// <param name="ProteinGrams">Protein in grams.</param>
/// <param name="CarbohydrateGrams">Carbohydrate in grams.</param>
/// <param name="FatGrams">Fat in grams.</param>
/// <param name="FibreGrams">Fibre in grams.</param>
/// <param name="SugarGrams">Sugar in grams.</param>
/// <param name="SodiumMilligrams">Sodium in milligrams.</param>
public sealed record NutrientProfile(
    decimal EnergyKilocalories,
    decimal ProteinGrams,
    decimal CarbohydrateGrams,
    decimal FatGrams,
    decimal FibreGrams,
    decimal SugarGrams,
    decimal SodiumMilligrams)
{
    /// <summary>Empty nutrient profile.</summary>
    public static NutrientProfile Zero { get; } = new(0m, 0m, 0m, 0m, 0m, 0m, 0m);

    /// <summary>Scales a profile by a serving mass relative to the per-100g basis.</summary>
    public NutrientProfile ForGrams(decimal grams)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(grams);
        var factor = grams / 100m;
        return new NutrientProfile(
            EnergyKilocalories * factor,
            ProteinGrams * factor,
            CarbohydrateGrams * factor,
            FatGrams * factor,
            FibreGrams * factor,
            SugarGrams * factor,
            SodiumMilligrams * factor);
    }

    /// <summary>Adds two nutrient profiles.</summary>
    public static NutrientProfile operator +(NutrientProfile left, NutrientProfile right) =>
        new(left.EnergyKilocalories + right.EnergyKilocalories,
            left.ProteinGrams + right.ProteinGrams,
            left.CarbohydrateGrams + right.CarbohydrateGrams,
            left.FatGrams + right.FatGrams,
            left.FibreGrams + right.FibreGrams,
            left.SugarGrams + right.SugarGrams,
            left.SodiumMilligrams + right.SodiumMilligrams);
}
