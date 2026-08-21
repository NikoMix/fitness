using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Onboarding;

/// <summary>Value-first first-run screen with a prominent no-credentials skip path.</summary>
public partial class WelcomePage : ContentPage
{
    private readonly WelcomeViewModel viewModel;

    /// <summary>Initialises the page for XAML route activation.</summary>
    public WelcomePage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>Initialises the page.</summary>
    /// <param name="viewModel">The welcome view model.</param>
    public WelcomePage(WelcomeViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Re-checked on every appearance rather than only at construction: the user may have
        // started the wizard, backed out, and returned, in which case the primary action should
        // now offer to resume rather than to start over.
        viewModel.Refresh();
    }

    private static WelcomeViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<WelcomeViewModel>()
        ?? throw new InvalidOperationException("The welcome view model could not be resolved.");
}
