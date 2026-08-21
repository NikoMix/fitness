using Forge.App.Features.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Nutrition;

/// <summary>Food search and logging page.</summary>
public partial class FoodLogPage : ContentPage
{
    /// <summary>Initialises the page.</summary>
    public FoodLogPage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>Initialises the page.</summary>
    public FoodLogPage(FoodLogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is FoodLogViewModel viewModel)
        {
            viewModel.LoadCommand.Execute(null);
        }
    }

    private static FoodLogViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<FoodLogViewModel>()
        ?? throw new InvalidOperationException("The Food Log view model could not be resolved.");
}
