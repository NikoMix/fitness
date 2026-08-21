namespace Forge.Core.Abstractions.Health;

/// <summary>
/// Canonical workout activity vocabulary shared by both platform mappers.
/// </summary>
/// <remarks>
/// Health Connect and HealthKit each have their own activity enumeration, and Forge's own plans use
/// free text. Without one agreed vocabulary in the middle, the two platform mappers drift: a
/// session recorded as "Strength" exports as strength training on iOS and as "other" on Android,
/// and nobody notices because neither mapper is wrong on its own terms.
/// </remarks>
public static class HealthWorkoutActivities
{
    /// <summary>Resistance training against external load.</summary>
    public const string StrengthTraining = "strength-training";

    /// <summary>Bodyweight-only resistance work.</summary>
    public const string Calisthenics = "calisthenics";

    /// <summary>Running, indoors or outdoors.</summary>
    public const string Running = "running";

    /// <summary>Walking or hiking.</summary>
    public const string Walking = "walking";

    /// <summary>Cycling, indoors or outdoors.</summary>
    public const string Cycling = "cycling";

    /// <summary>Rowing, on water or on a machine.</summary>
    public const string Rowing = "rowing";

    /// <summary>Swimming.</summary>
    public const string Swimming = "swimming";

    /// <summary>High-intensity interval training.</summary>
    public const string HighIntensityIntervalTraining = "hiit";

    /// <summary>Yoga.</summary>
    public const string Yoga = "yoga";

    /// <summary>Mobility, stretching or cool-down work.</summary>
    public const string Stretching = "stretching";

    /// <summary>Anything the vocabulary does not recognise.</summary>
    public const string Other = "other";

    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["strength"] = StrengthTraining,
        ["strengthtraining"] = StrengthTraining,
        ["traditionalstrengthtraining"] = StrengthTraining,
        ["weights"] = StrengthTraining,
        ["weightlifting"] = StrengthTraining,
        ["weighttraining"] = StrengthTraining,
        ["lifting"] = StrengthTraining,
        ["resistance"] = StrengthTraining,
        ["gym"] = StrengthTraining,
        ["powerlifting"] = StrengthTraining,
        ["bodyweight"] = Calisthenics,
        ["calisthenics"] = Calisthenics,
        ["functionalstrengthtraining"] = Calisthenics,
        ["run"] = Running,
        ["running"] = Running,
        ["jog"] = Running,
        ["jogging"] = Running,
        ["treadmill"] = Running,
        ["walk"] = Walking,
        ["walking"] = Walking,
        ["hike"] = Walking,
        ["hiking"] = Walking,
        ["bike"] = Cycling,
        ["biking"] = Cycling,
        ["cycle"] = Cycling,
        ["cycling"] = Cycling,
        ["spin"] = Cycling,
        ["spinning"] = Cycling,
        ["row"] = Rowing,
        ["rowing"] = Rowing,
        ["erg"] = Rowing,
        ["swim"] = Swimming,
        ["swimming"] = Swimming,
        ["hiit"] = HighIntensityIntervalTraining,
        ["highintensityintervaltraining"] = HighIntensityIntervalTraining,
        ["intervals"] = HighIntensityIntervalTraining,
        ["interval"] = HighIntensityIntervalTraining,
        ["circuit"] = HighIntensityIntervalTraining,
        ["circuits"] = HighIntensityIntervalTraining,
        ["metcon"] = HighIntensityIntervalTraining,
        ["yoga"] = Yoga,
        ["mobility"] = Stretching,
        ["stretch"] = Stretching,
        ["stretching"] = Stretching,
        ["cooldown"] = Stretching,
        ["other"] = Other
    };

    /// <summary>Maps free text onto the canonical vocabulary.</summary>
    /// <param name="activityType">Free-text activity name, possibly null, spaced or punctuated.</param>
    /// <returns>A canonical token; <see cref="Other"/> when nothing matches.</returns>
    public static string Normalise(string? activityType)
    {
        if (string.IsNullOrWhiteSpace(activityType))
        {
            return Other;
        }

        var condensed = Condense(activityType);
        return condensed.Length is 0
            ? Other
            : Synonyms.TryGetValue(condensed, out var canonical) ? canonical : Other;
    }

    private static string Condense(string value)
    {
        var buffer = new char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
            }
        }

        return new string(buffer, 0, length);
    }
}
