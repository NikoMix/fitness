namespace Forge.Core.Abstractions.Localization;

/// <summary>What Forge does when a string key has no translation in any culture.</summary>
public enum MissingLocalizedStringBehavior
{
    /// <summary>
    /// Return the key wrapped in exclamation marks, for example <c>!settings.language.title!</c>.
    /// </summary>
    /// <remarks>
    /// The one outcome that must never happen is a blank label. A blank looks like a layout bug,
    /// gets triaged as one, and can reach store review before anyone realises a string was never
    /// added. A visible marker names the missing key, so the fix is obvious from a screenshot.
    /// </remarks>
    Marker,

    /// <summary>Throw <see cref="MissingLocalizedStringException"/>.</summary>
    /// <remarks>
    /// Intended for tests and translation-coverage tooling. The app must not use this: an
    /// untranslated string is a defect, but it is not worth crashing a user's workout over.
    /// </remarks>
    Throw,
}

/// <summary>Configuration for <see cref="LocalizationService"/>.</summary>
public sealed record LocalizationOptions
{
    /// <summary>How a completely unresolved key is rendered. Defaults to a visible marker.</summary>
    public MissingLocalizedStringBehavior MissingStringBehavior { get; init; } = MissingLocalizedStringBehavior.Marker;

    /// <summary>The language used when the device language is not one Forge ships.</summary>
    public SupportedLanguage DefaultLanguage { get; init; } = ForgeLanguages.Default;

    /// <summary>The languages offered in the picker.</summary>
    public IReadOnlyList<SupportedLanguage> SupportedLanguages { get; init; } = ForgeLanguages.All;
}

/// <summary>Stable persistence keys owned by the localization feature.</summary>
/// <remarks>
/// These live beside the localization abstraction rather than in <c>ForgePreferenceKeys</c> so
/// that adding a language does not edit a file the Settings, Backup and Profile streams all
/// touch. The persisted value is either a language code (<c>de</c>) or <see cref="FollowSystem"/>.
/// </remarks>
public static class LocalizationPreferenceKeys
{
    /// <summary>Stored display-language selection.</summary>
    public const string Language = "forge.preferences.localization.language";

    /// <summary>The stored value meaning "follow whatever the device is set to".</summary>
    public const string FollowSystem = "system";
}
