namespace Forge.Core.Abstractions.Localization;

/// <summary>Every string key Forge ships, as compile-checked constants.</summary>
/// <remarks>
/// <para>
/// This is the strongly-typed accessor for the resource files. It deliberately lives in
/// <c>Forge.Core</c> rather than beside the <c>.resx</c> in the app head, for two reasons.
/// A generated designer class is only reachable from <c>Forge.App</c>, which no test project can
/// reference because the app head targets Android and iOS; and a key set that lives in the inner
/// layer can be asserted against the resource files by an ordinary unit test.
/// </para>
/// <para>
/// <c>ForgeStringResourcesTests</c> enforces an exact, two-way correspondence between these
/// constants and <c>src/Forge.App/Resources/Strings/ForgeStrings.resx</c>, so a key added here
/// without a string, a string added without a key, or a translation that drifts out of the
/// German file all fail the build rather than shipping as a marker on a screen.
/// </para>
/// <para>
/// Keys are dotted and lower case because they are read in XAML - <c>{loc:Translate
/// Key=settings.language.title}</c> - where a screaming constant name would be noise, and
/// because the prefix groups them by screen when grepping.
/// </para>
/// </remarks>
public static class ForgeStringKeys
{
    // ---- Product and shared vocabulary ----

    /// <summary>The product name. Not translated, but resolved through the same path.</summary>
    public const string AppName = "app.name";

    /// <summary>Cancel.</summary>
    public const string CommonCancel = "common.cancel";

    /// <summary>Save.</summary>
    public const string CommonSave = "common.save";

    /// <summary>Done.</summary>
    public const string CommonDone = "common.done";

    /// <summary>Back.</summary>
    public const string CommonBack = "common.back";

    /// <summary>The metric measurement system.</summary>
    public const string CommonMetric = "common.metric";

    /// <summary>The imperial measurement system.</summary>
    public const string CommonImperial = "common.imperial";

    // ---- Language settings (the pilot screen) ----

    /// <summary>Language settings page title.</summary>
    public const string LanguageSettingsTitle = "settings.language.title";

    /// <summary>Language settings page heading.</summary>
    public const string LanguageSettingsHeading = "settings.language.heading";

    /// <summary>Explains what the language setting covers.</summary>
    public const string LanguageSettingsDescription = "settings.language.description";

    /// <summary>Explains that units are a separate setting. Takes the unit system name.</summary>
    public const string LanguageSettingsUnitsNote = "settings.language.units-note";

    /// <summary>Label for the follow-the-device option.</summary>
    public const string LanguageSettingsFollowSystem = "settings.language.follow-system";

    /// <summary>Explains the follow-the-device option.</summary>
    public const string LanguageSettingsFollowSystemDescription = "settings.language.follow-system.description";

    /// <summary>Heading above the list of shipped languages.</summary>
    public const string LanguageSettingsAvailable = "settings.language.available";

    /// <summary>States the language in use. Takes the language's native name.</summary>
    public const string LanguageSettingsCurrent = "settings.language.current";

    /// <summary>Reassures the user that no restart is needed.</summary>
    public const string LanguageSettingsAppliesImmediately = "settings.language.applies-immediately";

    /// <summary>Heading above the formatting preview.</summary>
    public const string LanguageSettingsPreviewHeading = "settings.language.preview.heading";

    /// <summary>Preview row label: date.</summary>
    public const string LanguageSettingsPreviewDate = "settings.language.preview.date";

    /// <summary>Preview row label: number.</summary>
    public const string LanguageSettingsPreviewNumber = "settings.language.preview.number";

    /// <summary>Preview row label: percentage.</summary>
    public const string LanguageSettingsPreviewPercent = "settings.language.preview.percent";

    /// <summary>Preview row label: elapsed duration.</summary>
    public const string LanguageSettingsPreviewDuration = "settings.language.preview.duration";

    /// <summary>Preview row label: body weight.</summary>
    public const string LanguageSettingsPreviewBodyWeight = "settings.language.preview.body-weight";

    /// <summary>Preview row label: energy.</summary>
    public const string LanguageSettingsPreviewEnergy = "settings.language.preview.energy";

    /// <summary>Preview row label: the active measurement system.</summary>
    public const string LanguageSettingsUnitSystem = "settings.language.unit-system";
}
