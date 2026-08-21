using System.Globalization;
using Forge.Core.Abstractions.Preferences;

namespace Forge.Core.Abstractions.Localization;

/// <summary>Default localization service: resolution, fallback and persistence in one place.</summary>
/// <remarks>
/// <para>
/// Everything here is deliberately free of any UI framework so that the rules people actually
/// get wrong - the fallback chain, the missing-key policy, and the separation of language from
/// units - are covered by ordinary unit tests rather than by an emulator run.
/// </para>
/// <para>
/// The service never assigns <see cref="CultureInfo.DefaultThreadCurrentCulture"/> or any other
/// ambient culture. Mutating process-wide state from a library makes tests order-dependent and
/// hides who actually changed the culture. Applying the resolved culture to the process is the
/// app layer's job, driven by <see cref="LanguageChanged"/>.
/// </para>
/// </remarks>
public sealed class LocalizationService : ILocalizationService
{
    private readonly ILocalizedStringSource source;
    private readonly IPreferenceStore store;
    private readonly ISystemCultureProvider systemCulture;
    private readonly LocalizationOptions options;

    private string? selectedLanguageCode;
    private SupportedLanguage currentLanguage;
    private CultureInfo currentUICulture;
    private CultureInfo currentCulture;

    /// <summary>Creates the service and restores the persisted language choice.</summary>
    /// <param name="source">Supplies translated strings for an exact culture.</param>
    /// <param name="store">The existing Forge preference store. Language is not a second store.</param>
    /// <param name="systemCulture">Reports the device language captured at start-up.</param>
    /// <param name="options">Optional policy overrides.</param>
    public LocalizationService(
        ILocalizedStringSource source,
        IPreferenceStore store,
        ISystemCultureProvider systemCulture,
        LocalizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(systemCulture);

        this.source = source;
        this.store = store;
        this.systemCulture = systemCulture;
        this.options = options ?? new LocalizationOptions();

        var stored = store.GetString(LocalizationPreferenceKeys.Language, LocalizationPreferenceKeys.FollowSystem);
        selectedLanguageCode = NormalizeSelection(stored);

        // Assigned by Resolve, but the compiler cannot see through the call.
        currentLanguage = this.options.DefaultLanguage;
        currentUICulture = this.options.DefaultLanguage.Culture;
        currentCulture = this.options.DefaultLanguage.Culture;
        Resolve();
    }

    /// <inheritdoc />
    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;

    /// <inheritdoc />
    public IReadOnlyList<SupportedLanguage> SupportedLanguages => options.SupportedLanguages;

    /// <inheritdoc />
    public SupportedLanguage DefaultLanguage => options.DefaultLanguage;

    /// <inheritdoc />
    public string? SelectedLanguageCode => selectedLanguageCode;

    /// <inheritdoc />
    public bool FollowsSystemLanguage => selectedLanguageCode is null;

    /// <inheritdoc />
    public SupportedLanguage CurrentLanguage => currentLanguage;

    /// <inheritdoc />
    public CultureInfo CurrentUICulture => currentUICulture;

    /// <inheritdoc />
    public CultureInfo CurrentCulture => currentCulture;

    /// <inheritdoc />
    public bool IsRightToLeft => currentLanguage.IsRightToLeft;

    /// <inheritdoc />
    public void UseSystemLanguage() => Apply(null);

    /// <inheritdoc />
    public void UseLanguage(string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);

        var language = FindSupported(languageCode)
            ?? throw new ArgumentException(
                FormattableString.Invariant($"Forge ships no translation for '{languageCode}'."),
                nameof(languageCode));

        Apply(language.Code);
    }

    /// <inheritdoc />
    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        foreach (var culture in FallbackChain())
        {
            var value = source.Find(key, culture);

            // An empty entry counts as missing. A resource file with a blank value produces
            // exactly the silent blank label this policy exists to prevent, and blanks do get
            // committed - a translator clearing a cell is enough.
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return Missing(key);
    }

    /// <inheritdoc />
    public string GetString(string key, params object?[] arguments)
    {
        var template = GetString(key);

        return arguments is null || arguments.Length == 0
            ? template
            : string.Format(currentCulture, template, arguments);
    }

    private static string? NormalizeSelection(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)
            || string.Equals(stored, LocalizationPreferenceKeys.FollowSystem, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return stored;
    }

    /// <summary>Yields a culture and each of its parents, ending at the invariant culture.</summary>
    private static IEnumerable<CultureInfo> WithParents(CultureInfo culture)
    {
        var current = culture;

        while (true)
        {
            yield return current;

            if (current.Name.Length == 0)
            {
                yield break;
            }

            var parent = current.Parent;
            if (string.Equals(parent.Name, current.Name, StringComparison.Ordinal))
            {
                yield break;
            }

            current = parent;
        }
    }

    private SupportedLanguage? FindSupported(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var separator = code.IndexOfAny(['-', '_']);
        var language = separator < 0 ? code : code[..separator];

        return options.SupportedLanguages.FirstOrDefault(
            candidate => string.Equals(candidate.Code, language, StringComparison.OrdinalIgnoreCase));
    }

    private void Apply(string? languageCode)
    {
        var previousLanguage = currentLanguage;
        var previousCulture = currentCulture;

        selectedLanguageCode = languageCode;
        store.SetString(
            LocalizationPreferenceKeys.Language,
            languageCode ?? LocalizationPreferenceKeys.FollowSystem);

        Resolve();

        if (currentLanguage == previousLanguage && currentCulture.Equals(previousCulture))
        {
            return;
        }

        LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(currentLanguage, currentCulture));
    }

    private void Resolve()
    {
        var deviceCulture = systemCulture.Current;

        currentLanguage = FindSupported(selectedLanguageCode ?? deviceCulture.Name) ?? options.DefaultLanguage;

        // Keep the device's regional variant whenever it speaks the resolved language, so a
        // German choice on an Austrian device still says "Jänner" and uses Austrian date order.
        var deviceSpeaksCurrentLanguage = string.Equals(
            deviceCulture.TwoLetterISOLanguageName,
            currentLanguage.Code,
            StringComparison.OrdinalIgnoreCase);

        currentUICulture = deviceSpeaksCurrentLanguage ? deviceCulture : currentLanguage.Culture;

        // Formatting follows the device while Forge is following the device, even when the
        // device language has no translation: a Swedish user reading English still expects
        // Swedish dates and a comma decimal separator, not American ones.
        currentCulture = FollowsSystemLanguage || deviceSpeaksCurrentLanguage
            ? deviceCulture
            : currentLanguage.Culture;
    }

    private string Missing(string key) => options.MissingStringBehavior switch
    {
        MissingLocalizedStringBehavior.Throw => throw new MissingLocalizedStringException(key, currentUICulture),
        _ => string.Concat("!", key, "!"),
    };

    /// <summary>The ordered cultures a lookup consults: current, its parents, then the default language.</summary>
    private IEnumerable<CultureInfo> FallbackChain()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var culture in WithParents(currentUICulture))
        {
            if (seen.Add(culture.Name))
            {
                yield return culture;
            }
        }

        foreach (var culture in WithParents(options.DefaultLanguage.Culture))
        {
            if (seen.Add(culture.Name))
            {
                yield return culture;
            }
        }
    }
}
