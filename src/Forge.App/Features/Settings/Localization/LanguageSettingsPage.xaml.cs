using Forge.Core.Abstractions.Localization;

namespace Forge.App.Features.Settings.Localization;

/// <summary>Display-language picker, and the pilot for Forge's localization mechanism.</summary>
public partial class LanguageSettingsPage : ContentPage
{
    private readonly LanguageSettingsPageViewModel viewModel;
    private readonly ILocalizationService localization;

    /// <summary>Creates the page.</summary>
    /// <param name="viewModel">Supplies the formatted and composite strings.</param>
    /// <param name="localization">Reports whether the current language is right to left.</param>
    public LanguageSettingsPage(LanguageSettingsPageViewModel viewModel, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(localization);

        InitializeComponent();

        this.viewModel = viewModel;
        this.localization = localization;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Forge ships no right-to-left language yet, so this always resolves left to right today.
        // It is wired anyway because it is the single line each page needs, and doing it here
        // proves the readiness work in docs/localization/rtl-readiness.md is a layout exercise
        // rather than an architectural one.
        FlowDirection = localization.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        viewModel.Attach();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        viewModel.Detach();
        base.OnDisappearing();
    }
}
