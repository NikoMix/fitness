namespace Forge.Domain.Training;

/// <summary>
/// The equipment a trainee can actually reach right now.
/// </summary>
/// <remarks>
/// <para>
/// Substitution is only useful if it is honest, and honesty here depends entirely on knowing
/// what the person has in front of them. Equipment therefore gets its own type rather than
/// being passed around as a loose string collection: the normalisation rules, the
/// always-available bodyweight case, and the free-text synonyms a user types during onboarding
/// all have to agree, and they only agree if they live in one place.
/// </para>
/// <para>
/// Matching is deliberately case-insensitive and whitespace-tolerant. The catalogue writes
/// "Pull-up bar" while a user is just as likely to type "pull up bar", and refusing to match
/// those would silently hide every alternative the person could actually perform.
/// </para>
/// </remarks>
public sealed class EquipmentAvailability
{
    /// <summary>The canonical name for needing no equipment at all.</summary>
    public const string Bodyweight = "Bodyweight";

    /// <summary>
    /// Free-text spellings that mean "no equipment".
    /// </summary>
    /// <remarks>
    /// Onboarding collects equipment as free text, so these arrive from real users rather than
    /// from the catalogue. Folding them into the canonical name stops a profile that says
    /// "none" from being read as owning a device literally called "none".
    /// </remarks>
    private static readonly string[] BodyweightSynonyms =
    [
        "none",
        "no equipment",
        "no kit",
        "body weight",
        "bodyweight",
        "nothing"
    ];

    private static readonly char[] DeclarationSeparators = [',', ';', '\n', '\r', '|'];

    private readonly HashSet<string> equipment;

    private EquipmentAvailability(HashSet<string> equipment) => this.equipment = equipment;

    /// <summary>Availability for someone training with nothing but their own body.</summary>
    public static EquipmentAvailability BodyweightOnly { get; } =
        new(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Bodyweight });

    /// <summary>Every distinct item available, always including <see cref="Bodyweight"/>.</summary>
    public IReadOnlyCollection<string> Items => equipment;

    /// <summary>Whether anything beyond bodyweight is available.</summary>
    public bool HasEquipment => equipment.Count > 1;

    /// <summary>Builds availability from individual equipment names.</summary>
    /// <param name="declared">Equipment names, in any casing. Blank entries are ignored.</param>
    /// <returns>Availability including bodyweight.</returns>
    public static EquipmentAvailability From(IEnumerable<string?> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Bodyweight };
        foreach (var item in declared)
        {
            items.Add(Normalise(item));
        }

        return new EquipmentAvailability(items);
    }

    /// <summary>
    /// Builds availability from a single delimited declaration.
    /// </summary>
    /// <remarks>
    /// The user profile persists equipment as one comma-separated string, so parsing it is a
    /// recurring need rather than a caller's private concern.
    /// </remarks>
    /// <param name="declaration">A comma, semicolon, pipe or newline separated list.</param>
    /// <returns>Availability including bodyweight.</returns>
    public static EquipmentAvailability FromDeclaration(string? declaration)
        => string.IsNullOrWhiteSpace(declaration)
            ? BodyweightOnly
            : From(declaration.Split(DeclarationSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Reduces an equipment name to its canonical form.</summary>
    /// <param name="equipment">The raw name, which may be blank for bodyweight movements.</param>
    /// <returns>The canonical name, or <see cref="Bodyweight"/> when nothing is required.</returns>
    public static string Normalise(string? equipment)
    {
        if (string.IsNullOrWhiteSpace(equipment))
        {
            return Bodyweight;
        }

        var trimmed = equipment.Trim();
        return BodyweightSynonyms.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            ? Bodyweight
            : trimmed;
    }

    /// <summary>Whether an equipment name is available.</summary>
    /// <param name="requiredEquipment">The name to test. Blank counts as bodyweight.</param>
    /// <returns><see langword="true"/> when the item is available.</returns>
    public bool Allows(string? requiredEquipment) => equipment.Contains(Normalise(requiredEquipment));

    /// <summary>Whether an exercise can be performed with what is available.</summary>
    /// <param name="exercise">The exercise to test.</param>
    /// <returns><see langword="true"/> when its equipment is available.</returns>
    public bool Allows(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        return Allows(exercise.Equipment);
    }

    /// <summary>Returns availability with one more item added.</summary>
    /// <param name="additionalEquipment">The item to add.</param>
    /// <returns>A new availability set. The original is unchanged.</returns>
    public EquipmentAvailability With(string? additionalEquipment)
        => new(new HashSet<string>(equipment, StringComparer.OrdinalIgnoreCase) { Normalise(additionalEquipment) });
}
