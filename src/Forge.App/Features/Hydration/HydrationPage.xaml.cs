using Forge.App.Features.Hydration.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Hydration;

/// <summary>Hydration logging page.</summary>
public partial class HydrationPage : ContentPage
{
    /// <summary>Initialises the page.</summary>
    public HydrationPage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>Initialises the page.</summary>
    public HydrationPage(HydrationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is HydrationViewModel viewModel)
        {
            viewModel.LoadCommand.Execute(null);
        }
    }

    private static HydrationViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<HydrationViewModel>()
        ?? throw new InvalidOperationException("The Hydration view model could not be resolved.");
}
