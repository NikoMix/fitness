using Forge.Domain.Measurement;

namespace Forge.Domain.Workout;

/// <summary>
/// The bar and plates a user actually owns or has access to.
/// </summary>
/// <remarks>
/// <para>
/// A plate calculator that assumes a full commercial rack is worse than no calculator at all:
/// it confidently tells a home lifter to load a 15 kg plate they do not own. Inventory is
/// therefore an explicit input, and every result is derived only from plates that exist.
/// </para>
/// <para>
/// Plates are counted in pairs because a barbell must be loaded symmetrically. Counting single
/// plates would let the calculator produce an unloadable answer such as three 20 kg plates.
/// </para>
/// </remarks>
/// <param name="BarbellWeight">The bar the user is loading.</param>
/// <param name="Plates">Available plate denominations and pair counts.</param>
public sealed record PlateInventory(Mass BarbellWeight, IReadOnlyList<AvailablePlate> Plates)
{
    /// <summary>A commercial metric gym: 20 kg bar, plates from 20 kg down to 1.25 kg.</summary>
    public static PlateInventory MetricDefault { get; } = new(
        PlateCalculator.StandardBarbell,
        [
            new AvailablePlate(Mass.FromKilograms(25m), 2),
            new AvailablePlate(Mass.FromKilograms(20m), 4),
            new AvailablePlate(Mass.FromKilograms(15m), 2),
            new AvailablePlate(Mass.FromKilograms(10m), 2),
            new AvailablePlate(Mass.FromKilograms(5m), 2),
            new AvailablePlate(Mass.FromKilograms(2.5m), 2),
            new AvailablePlate(Mass.FromKilograms(1.25m), 2)
        ]);

    /// <summary>A commercial imperial gym: 45 lb bar, plates from 45 lb down to 2.5 lb.</summary>
    public static PlateInventory ImperialDefault { get; } = new(
        Mass.FromPounds(45m),
        [
            new AvailablePlate(Mass.FromPounds(45m), 4),
            new AvailablePlate(Mass.FromPounds(35m), 2),
            new AvailablePlate(Mass.FromPounds(25m), 2),
            new AvailablePlate(Mass.FromPounds(10m), 2),
            new AvailablePlate(Mass.FromPounds(5m), 2),
            new AvailablePlate(Mass.FromPounds(2.5m), 2)
        ]);

    /// <summary>Returns the same inventory loaded onto a different bar.</summary>
    /// <param name="barbellWeight">The bar to use.</param>
    /// <returns>A new inventory.</returns>
    public PlateInventory WithBarbell(Mass barbellWeight) => this with { BarbellWeight = barbellWeight };

    /// <summary>Returns the same inventory with one denomination's pair count changed.</summary>
    /// <param name="plateWeight">The denomination to change.</param>
    /// <param name="pairCount">How many pairs are available. Zero removes the denomination.</param>
    /// <returns>A new inventory, ordered heaviest first.</returns>
    public PlateInventory WithPlatePairs(Mass plateWeight, int pairCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pairCount);

        var remaining = Plates.Where(plate => plate.Weight != plateWeight).ToList();
        if (pairCount > 0)
        {
            remaining.Add(new AvailablePlate(plateWeight, pairCount));
        }

        return this with { Plates = [.. remaining.OrderByDescending(plate => plate.Weight.Kilograms)] };
    }

    /// <summary>Calculates the loading for a target weight using this inventory.</summary>
    /// <param name="targetLoad">The desired total on the bar.</param>
    /// <returns>The closest achievable loading.</returns>
    public PlateLoadingResult Calculate(Mass targetLoad)
        => PlateCalculator.Calculate(targetLoad, BarbellWeight, Plates);
}
