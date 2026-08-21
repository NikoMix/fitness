using System.Globalization;
using Forge.Core.Abstractions.Localization;

namespace Forge.App.Services.Localization;

/// <summary>Applies the resolved cultures to the running process and refreshes bound labels.</summary>
/// <remarks>
/// <para>
/// <see cref="LocalizationService"/> resolves which language and culture apply but deliberately
/// mutates nothing global, so that its rules stay unit-testable and test ordering cannot matter.
/// Actually moving the process onto that culture is a hosting concern, and this is where it
/// happens.
/// </para>
/// <para>
/// Both ambient cultures are set. <see cref="CultureInfo.DefaultThreadCurrentCulture"/> covers
/// threads created later - background work, timers, the data layer - while assigning the current
/// thread's culture covers the UI thread that is already running. Setting only the defaults is a
/// classic half-fix: the app formats correctly everywhere except the screen the user is looking
/// at.
/// </para>
/// </remarks>
public sealed class LocalizationRuntime : IDisposable
{
    private readonly ILocalizationService localization;
    private readonly LocalizedStrings strings;
    private bool started;

    /// <summary>Creates the runtime.</summary>
    /// <param name="localization">Resolves the language and cultures.</param>
    /// <param name="strings">The bindable string view refreshed after a change.</param>
    public LocalizationRuntime(ILocalizationService localization, LocalizedStrings strings)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(strings);

        this.localization = localization;
        this.strings = strings;
    }

    /// <summary>Applies the stored language and keeps applying every later change.</summary>
    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        localization.LanguageChanged += OnLanguageChanged;
        ApplyCulture();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!started)
        {
            return;
        }

        localization.LanguageChanged -= OnLanguageChanged;
        started = false;
    }

    private void ApplyCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = localization.CurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = localization.CurrentUICulture;
        CultureInfo.CurrentCulture = localization.CurrentCulture;
        CultureInfo.CurrentUICulture = localization.CurrentUICulture;
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        if (MainThread.IsMainThread)
        {
            ApplyCulture();
            strings.Refresh();
            return;
        }

        // Bindings must be invalidated on the UI thread, and the UI thread's own culture has to
        // be assigned from the UI thread - assigning it from a background thread would leave the
        // visible screen formatting with the previous culture.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplyCulture();
            strings.Refresh();
        });
    }
}
