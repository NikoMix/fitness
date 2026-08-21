using System.Globalization;

namespace Forge.Core.Abstractions.Localization;

/// <summary>Reports the language the device itself is configured for.</summary>
/// <remarks>
/// This is not the same as reading <see cref="CultureInfo.CurrentUICulture"/> on demand. Once
/// Forge applies a chosen language it overwrites the thread and default cultures, so a later
/// read would return Forge's own choice and "follow the device" would lock itself to whatever
/// the user last picked. Implementations must capture the device value once, before any
/// override is applied.
/// </remarks>
public interface ISystemCultureProvider
{
    /// <summary>The culture the device was configured for at process start.</summary>
    CultureInfo Current { get; }
}

/// <summary>Captures the ambient culture once, at construction.</summary>
public sealed class SystemCultureProvider : ISystemCultureProvider
{
    /// <summary>Captures the current UI culture as the device culture.</summary>
    /// <remarks>Construct this before anything assigns a culture override.</remarks>
    public SystemCultureProvider()
        : this(CultureInfo.CurrentUICulture)
    {
    }

    /// <summary>Captures an explicit device culture.</summary>
    /// <param name="culture">The device culture.</param>
    public SystemCultureProvider(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        Current = culture;
    }

    /// <inheritdoc />
    public CultureInfo Current { get; }
}

/// <summary>Describes a language change.</summary>
/// <param name="language">The language now in effect.</param>
/// <param name="culture">The culture now used for formatting.</param>
public sealed class LanguageChangedEventArgs(SupportedLanguage language, CultureInfo culture) : EventArgs
{
    /// <summary>The language now in effect.</summary>
    public SupportedLanguage Language { get; } = language;

    /// <summary>The culture now used for dates, numbers and units.</summary>
    public CultureInfo Culture { get; } = culture;
}

/// <summary>Resolves translated strings and the cultures Forge formats with.</summary>
/// <remarks>
/// <para>
/// Two cultures are exposed deliberately, mirroring the split the base class library makes.
/// <see cref="CurrentUICulture"/> selects the translation; <see cref="CurrentCulture"/> selects
/// date, number and separator conventions. A Swedish device gets Swedish dates with English
/// text, because Forge has no Swedish translation but has no reason to impose American dates.
/// </para>
/// <para>
/// Neither culture has anything to do with measurement units. A German user may train in
/// pounds and an American in kilograms; that choice lives in <c>IForgePreferences.UnitSystem</c>
/// and is never inferred from language. See <see cref="ILocalizedValueFormatter"/>.
/// </para>
/// </remarks>
public interface ILocalizationService
{
    /// <summary>Raised after the effective language or culture changes.</summary>
    event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    /// <summary>The languages offered in the picker.</summary>
    IReadOnlyList<SupportedLanguage> SupportedLanguages { get; }

    /// <summary>The language used when the device language is not one Forge ships.</summary>
    SupportedLanguage DefaultLanguage { get; }

    /// <summary>The explicitly chosen language code, or null when following the device.</summary>
    string? SelectedLanguageCode { get; }

    /// <summary>Whether Forge is following the device language rather than an explicit choice.</summary>
    bool FollowsSystemLanguage { get; }

    /// <summary>The language whose translations are in use.</summary>
    SupportedLanguage CurrentLanguage { get; }

    /// <summary>The culture used to look up translations.</summary>
    CultureInfo CurrentUICulture { get; }

    /// <summary>The culture used to format dates, numbers and unit values.</summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>Whether the current language is written right to left.</summary>
    bool IsRightToLeft { get; }

    /// <summary>Follows the device language from now on, and persists that choice.</summary>
    void UseSystemLanguage();

    /// <summary>Switches to an explicit language and persists that choice.</summary>
    /// <param name="languageCode">A shipped language code, for example <c>de</c>.</param>
    /// <exception cref="ArgumentException">The language is not one Forge ships.</exception>
    void UseLanguage(string languageCode);

    /// <summary>Resolves a translated string.</summary>
    /// <param name="key">A key from <see cref="ForgeStringKeys"/>.</param>
    /// <returns>The translation, or a visible marker. Never null and never empty.</returns>
    string GetString(string key);

    /// <summary>Resolves a translated composite format string and fills it in.</summary>
    /// <param name="key">A key from <see cref="ForgeStringKeys"/>.</param>
    /// <param name="arguments">Format arguments, formatted with <see cref="CurrentCulture"/>.</param>
    /// <returns>The formatted translation, or a visible marker. Never null and never empty.</returns>
    string GetString(string key, params object?[] arguments);
}
