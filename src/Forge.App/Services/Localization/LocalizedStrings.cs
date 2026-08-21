using System.ComponentModel;
using Forge.Core.Abstractions.Localization;

namespace Forge.App.Services.Localization;

/// <summary>Bindable access to translated strings, refreshed when the language changes.</summary>
/// <remarks>
/// <para>
/// This is what makes switching language without a restart possible. A XAML binding to the
/// indexer - <c>{Binding [settings.language.title], Source=...}</c>, produced by
/// <see cref="TranslateExtension"/> - re-reads its value whenever this object reports that its
/// indexer changed. Rebuilding pages or restarting the app is not needed, and neither is any
/// per-page code.
/// </para>
/// <para>
/// <see cref="Current"/> exists because a XAML markup extension is constructed by the parser,
/// not by the container, so it cannot take a constructor dependency. The instance is still a
/// normal DI singleton; <c>AddLocalizationFeature</c> publishes it here as the one instance the
/// parser may reach.
/// </para>
/// </remarks>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    private static LocalizedStrings? current;

    private readonly ILocalizationService localization;

    /// <summary>Creates a bindable view over the localization service.</summary>
    /// <param name="localization">Resolves keys to translated strings.</param>
    public LocalizedStrings(ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        this.localization = localization;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The instance XAML markup extensions bind against.</summary>
    /// <exception cref="InvalidOperationException">The localization feature was never registered.</exception>
    public static LocalizedStrings Current => current
        ?? throw new InvalidOperationException(
            "Localized XAML was inflated before the localization feature was registered. Add " +
            ".AddLocalizationFeature() to FeatureRegistration.AddForgeFeatures().");

    /// <summary>The translated string for a key, or a visible marker. Never blank.</summary>
    /// <param name="key">A key from <see cref="ForgeStringKeys"/>.</param>
    public string this[string key] => localization.GetString(key);

    /// <summary>Publishes this instance as the one XAML binds to.</summary>
    /// <param name="instance">The container-owned singleton.</param>
    public static void UseAsCurrent(LocalizedStrings instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        current = instance;
    }

    /// <summary>Tells every bound label to re-read its string.</summary>
    /// <remarks>
    /// An empty property name is the base class library's convention for "all properties
    /// changed", and MAUI's binding engine honours it for indexers too. Naming individual
    /// indexer entries would mean tracking which keys are currently on screen, which is more
    /// bookkeeping than a language switch is worth.
    /// </remarks>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
