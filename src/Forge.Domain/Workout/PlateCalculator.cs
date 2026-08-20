using Forge.Domain.Measurement;

namespace Forge.Domain.Workout;

/// <summary>Calculates symmetrical barbell loading for the user's available plates.</summary>
public sealed class PlateCalculator
{
    private const decimal Unit = 0.001m;

    /// <summary>Standard 20 kg Olympic bar.</summary>
    public static Mass StandardBarbell => Mass.FromKilograms(20m);

    /// <summary>15 kg Olympic bar, commonly used for women's competition bars.</summary>
    public static Mass WomensBarbell => Mass.FromKilograms(15m);

    /// <summary>Typical fixed/EZ curl bar.</summary>
    public static Mass EzCurlBar => Mass.FromKilograms(10m);

    /// <summary>Computes the closest achievable load.</summary>
    public static PlateLoadingResult Calculate(
        Mass targetLoad,
        Mass barbellWeight,
        IEnumerable<AvailablePlate> availablePlates)
    {
        ArgumentNullException.ThrowIfNull(availablePlates);

        var plates = availablePlates
            .Where(p => p.PairCount > 0 && p.Weight.Kilograms > 0m)
            .OrderByDescending(p => p.Weight.Kilograms)
            .ToArray();

        var targetPerSide = (targetLoad.Kilograms - barbellWeight.Kilograms) / 2m;
        if (targetPerSide <= 0m || plates.Length == 0)
        {
            return PlateLoadingResult.Create(targetLoad, barbellWeight, []);
        }

        var combinations = new Dictionary<int, List<Mass>> { [0] = [] };
        foreach (var plate in plates)
        {
            for (var count = 0; count < plate.PairCount; count++)
            {
                var snapshot = combinations.ToArray();
                var plateUnits = ToUnits(plate.Weight.Kilograms);
                foreach (var (load, existing) in snapshot)
                {
                    var candidateLoad = load + plateUnits;
                    if (combinations.ContainsKey(candidateLoad))
                    {
                        continue;
                    }

                    var candidate = new List<Mass>(existing) { plate.Weight };
                    combinations[candidateLoad] = candidate;
                }
            }
        }

        var targetUnits = ToUnits(targetPerSide);
        var best = combinations.Keys
            .OrderBy(load => Math.Abs(load - targetUnits))
            .ThenByDescending(load => load <= targetUnits)
            .ThenBy(load => load)
            .First();

        var resultPlates = combinations[best]
            .OrderByDescending(m => m.Kilograms)
            .ToArray();

        return PlateLoadingResult.Create(targetLoad, barbellWeight, resultPlates);
    }

    private static int ToUnits(decimal kilograms) => (int)Math.Round(kilograms / Unit, MidpointRounding.AwayFromZero);
}

/// <summary>A plate denomination and how many matching left/right pairs are available.</summary>
public readonly record struct AvailablePlate(Mass Weight, int PairCount);

/// <summary>Result of a plate loading calculation.</summary>
public sealed record PlateLoadingResult(
    Mass TargetLoad,
    Mass AchievableLoad,
    Mass BarbellWeight,
    Mass PerSideLoad,
    IReadOnlyList<Mass> PlatesPerSide,
    bool IsExact,
    Mass Difference)
{
    internal static PlateLoadingResult Create(Mass targetLoad, Mass barbellWeight, IReadOnlyList<Mass> platesPerSide)
    {
        var perSide = Mass.FromKilograms(platesPerSide.Sum(p => p.Kilograms));
        var achievable = Mass.FromKilograms(barbellWeight.Kilograms + (perSide.Kilograms * 2m));
        var difference = Mass.FromKilograms(Math.Abs(targetLoad.Kilograms - achievable.Kilograms));

        return new PlateLoadingResult(
            targetLoad,
            achievable,
            barbellWeight,
            perSide,
            platesPerSide,
            difference.Kilograms == 0m,
            difference);
    }
}
