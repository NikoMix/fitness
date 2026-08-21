using System.Globalization;
using Forge.Core.Abstractions.Preferences;
using Forge.Domain.Measurement;
using Forge.Domain.Workout;

namespace Forge.App.Features.Workout;

/// <summary>
/// Remembers how long the user rests after each individual exercise.
/// </summary>
/// <remarks>
/// Rest length is a property of the movement, not of the session, so it belongs in preferences
/// rather than in the workout snapshot: a user who rests four minutes after a heavy squat wants
/// that again next Monday, without reconfiguring it.
/// </remarks>
public interface IExerciseRestPreferences
{
    /// <summary>The default used when an exercise has no specific setting.</summary>
    RestPrescription AppDefault { get; }

    /// <summary>Reads the prescription for one exercise, falling back to the default.</summary>
    /// <param name="exerciseId">The exercise.</param>
    /// <returns>The prescription to use.</returns>
    RestPrescription Resolve(Guid exerciseId);

    /// <summary>Whether the exercise has its own setting rather than the default.</summary>
    /// <param name="exerciseId">The exercise.</param>
    /// <returns><see langword="true"/> when a specific value is stored.</returns>
    bool HasOverride(Guid exerciseId);

    /// <summary>Stores the working-set rest for one exercise.</summary>
    /// <param name="exerciseId">The exercise.</param>
    /// <param name="workingSetRest">The desired rest after a working set.</param>
    /// <returns>The stored prescription after clamping.</returns>
    RestPrescription SetWorkingSetRest(Guid exerciseId, TimeSpan workingSetRest);

    /// <summary>Removes an exercise's setting so it follows the default again.</summary>
    /// <param name="exerciseId">The exercise.</param>
    void Clear(Guid exerciseId);
}

/// <inheritdoc />
internal sealed class ExerciseRestPreferences(IPreferenceStore store, IForgePreferences preferences) : IExerciseRestPreferences
{
    private const string KeyPrefix = "forge.workout.rest-seconds.";
    private const int Unset = 0;

    /// <inheritdoc />
    public RestPrescription AppDefault => RestPrescription.FromWorkingSetRest(preferences.RestTimerDefaultDuration);

    /// <inheritdoc />
    public RestPrescription Resolve(Guid exerciseId)
    {
        var seconds = store.GetInt32(KeyFor(exerciseId), Unset);
        return seconds <= Unset ? AppDefault : RestPrescription.FromWorkingSetRest(TimeSpan.FromSeconds(seconds));
    }

    /// <inheritdoc />
    public bool HasOverride(Guid exerciseId) => store.GetInt32(KeyFor(exerciseId), Unset) > Unset;

    /// <inheritdoc />
    public RestPrescription SetWorkingSetRest(Guid exerciseId, TimeSpan workingSetRest)
    {
        var prescription = RestPrescription.FromWorkingSetRest(workingSetRest);
        var seconds = (int)Math.Round(prescription.WorkingSetRest.TotalSeconds, MidpointRounding.AwayFromZero);
        store.SetInt32(KeyFor(exerciseId), seconds);
        return prescription;
    }

    /// <inheritdoc />
    public void Clear(Guid exerciseId) => store.SetInt32(KeyFor(exerciseId), Unset);

    private static string KeyFor(Guid exerciseId)
        => string.Concat(KeyPrefix, exerciseId.ToString("N", CultureInfo.InvariantCulture));
}

/// <summary>Remembers the bar and plates the user actually has access to.</summary>
public interface IPlateInventoryStore
{
    /// <summary>Loads the stored inventory, or the unit-appropriate default.</summary>
    /// <returns>The inventory to calculate with.</returns>
    PlateInventory Load();

    /// <summary>Stores an inventory.</summary>
    /// <param name="inventory">The inventory to remember.</param>
    void Save(PlateInventory inventory);
}

/// <inheritdoc />
internal sealed class PlateInventoryStore(IPreferenceStore store, IForgePreferences preferences) : IPlateInventoryStore
{
    private const string BarbellKey = "forge.workout.plate-inventory.barbell-grams";
    private const string PlatesKey = "forge.workout.plate-inventory.plates";
    private const decimal GramsPerKilogram = 1000m;

    /// <inheritdoc />
    public PlateInventory Load()
    {
        var fallback = preferences.MassUnit == MassUnitPreference.Pounds
            ? PlateInventory.ImperialDefault
            : PlateInventory.MetricDefault;

        var serialised = store.GetString(PlatesKey, string.Empty);
        if (string.IsNullOrWhiteSpace(serialised))
        {
            return fallback;
        }

        var plates = ParsePlates(serialised);
        if (plates.Count == 0)
        {
            return fallback;
        }

        var barbellGrams = store.GetInt32(BarbellKey, 0);
        var barbell = barbellGrams > 0
            ? Mass.FromKilograms(barbellGrams / GramsPerKilogram)
            : fallback.BarbellWeight;

        return new PlateInventory(barbell, plates);
    }

    /// <inheritdoc />
    public void Save(PlateInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        store.SetInt32(BarbellKey, ToGrams(inventory.BarbellWeight));
        store.SetString(
            PlatesKey,
            string.Join(
                ';',
                inventory.Plates
                    .Where(plate => plate.PairCount > 0)
                    .Select(plate => string.Create(
                        CultureInfo.InvariantCulture,
                        $"{ToGrams(plate.Weight)}x{plate.PairCount}"))));
    }

    // Whole grams keep the stored form exact for every plate denomination in use, metric or
    // imperial, without depending on a culture-sensitive decimal round trip.
    private static int ToGrams(Mass mass) => (int)Math.Round(mass.Kilograms * GramsPerKilogram, MidpointRounding.AwayFromZero);

    private static List<AvailablePlate> ParsePlates(string serialised)
    {
        var plates = new List<AvailablePlate>();
        foreach (var entry in serialised.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('x', 2);
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var grams)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pairs)
                || grams <= 0
                || pairs <= 0)
            {
                continue;
            }

            plates.Add(new AvailablePlate(Mass.FromKilograms(grams / GramsPerKilogram), pairs));
        }

        return [.. plates.OrderByDescending(plate => plate.Weight.Kilograms)];
    }
}
