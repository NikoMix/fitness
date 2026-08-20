using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Domain.Training;

namespace Forge.Infrastructure.Content;

/// <summary>Loads Forge seed exercise content embedded in the app package.</summary>
/// <remarks>
/// Catalogue text is product content, not anonymous data. Every description, cue, step, and
/// mistake in the embedded JSON must be original Forge writing or explicitly licensed for this
/// use. Do not paste exercise databases from websites, apps, spreadsheets, or model output that
/// reproduces copyrighted source text.
/// </remarks>
public static class SeedCatalogue
{
    private const string ResourceName = "Forge.Infrastructure.Content.exercise-catalogue.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Lazy<IReadOnlyList<Exercise>> CachedExercises = new(LoadExercises);

    /// <summary>Gets the parsed offline exercise catalogue.</summary>
    public static IReadOnlyList<Exercise> Exercises => CachedExercises.Value;

    /// <summary>
    /// Opens the raw embedded catalogue JSON.
    /// </summary>
    /// <remarks>
    /// Exposed so startup can hand the stream to <c>SeedContentImporter</c>, which performs the
    /// versioned, idempotent import that preserves user-created exercises. Reusing that path
    /// avoids a second, subtly different seeding implementation living in the app head.
    /// </remarks>
    /// <returns>A readable stream over the embedded catalogue. The caller owns disposal.</returns>
    public static Stream OpenCatalogueStream()
    {
        var assembly = typeof(SeedCatalogue).Assembly;
        return assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded exercise catalogue '{ResourceName}' was not found in {assembly.GetName().Name}.");
    }

    /// <summary>Finds an exercise by its display name.</summary>
    public static Exercise? FindByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Exercises.FirstOrDefault(exercise => string.Equals(exercise.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Exercise> LoadExercises()
    {
        var assembly = typeof(SeedCatalogue).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded exercise catalogue '{ResourceName}' was not found in {assembly.GetName().Name}.");

        var catalogue = JsonSerializer.Deserialize<CatalogueDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("The embedded exercise catalogue could not be parsed.");

        if (catalogue.Exercises.Count == 0)
        {
            throw new InvalidOperationException("The embedded exercise catalogue is empty.");
        }

        if (string.IsNullOrWhiteSpace(catalogue.Provenance)
            || !catalogue.Provenance.Contains("Original Forge", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The embedded exercise catalogue must declare original-content provenance.");
        }

        return catalogue.Exercises
            .Select(item => item.ToExercise())
            .OrderBy(exercise => exercise.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record CatalogueDocument(
        int Version,
        string Provenance,
        List<ExerciseCatalogueItem> Exercises);

    private sealed record ExerciseCatalogueItem(
        string Name,
        MovementPattern Pattern,
        string PrimaryMuscle,
        List<string> SecondaryMuscles,
        string? Equipment,
        ExerciseDifficulty Difficulty,
        ExerciseForceType ForceType,
        List<string> ExecutionSteps,
        List<string> CommonMistakes,
        List<string> CoachingCues,
        List<string> SafetyNotes,
        bool IsUnilateral,
        string Provenance)
    {
        public Exercise ToExercise()
        {
            if (string.IsNullOrWhiteSpace(Provenance)
                || !Provenance.Contains("Original Forge", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Exercise '{Name}' must declare original-content provenance.");
            }

            return new Exercise
            {
                Name = Name,
                Pattern = Pattern,
                PrimaryMuscle = PrimaryMuscle,
                SecondaryMuscles = SecondaryMuscles,
                Equipment = Equipment,
                Difficulty = Difficulty,
                ForceType = ForceType,
                ExecutionSteps = ExecutionSteps,
                CommonMistakes = CommonMistakes,
                CoachingCues = CoachingCues,
                SafetyNotes = SafetyNotes,
                IsUnilateral = IsUnilateral
            };
        }
    }
}
