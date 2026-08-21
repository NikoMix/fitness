using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Onboarding;

/// <summary>Multi-step onboarding goal wizard.</summary>
public partial class GoalWizardPage : ContentPage
{
    /// <summary>Initialises the page for XAML route activation.</summary>
    public GoalWizardPage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>Initialises the page.</summary>
    public GoalWizardPage(GoalWizardViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        BindingContext = viewModel;
    }

    private static GoalWizardViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<GoalWizardViewModel>()
        ?? throw new InvalidOperationException("The goal wizard view model could not be resolved.");
}
