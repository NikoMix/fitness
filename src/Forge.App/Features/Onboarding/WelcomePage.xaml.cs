using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Onboarding;

/// <summary>Value-first first-run screen with a prominent no-credentials skip path.</summary>
public partial class WelcomePage : ContentPage
{
    /// <summary>Initialises the page for XAML route activation.</summary>
    public WelcomePage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>Initialises the page.</summary>
    public WelcomePage(WelcomeViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        BindingContext = viewModel;
    }

    private static WelcomeViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<WelcomeViewModel>()
        ?? throw new InvalidOperationException("The welcome view model could not be resolved.");
}
