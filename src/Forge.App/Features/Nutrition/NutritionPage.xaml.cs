using Forge.App.Features.Nutrition.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Nutrition;

/// <summary>Nutrition hub showing macro split, budget and meals.</summary>
public partial class NutritionPage : ContentPage
{
    /// <summary>Initialises the page.</summary>
    public NutritionPage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>Initialises the page.</summary>
    public NutritionPage(NutritionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is NutritionViewModel viewModel)
        {
            viewModel.LoadCommand.Execute(null);
        }
    }

    private static NutritionViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<NutritionViewModel>()
        ?? throw new InvalidOperationException("The Nutrition view model could not be resolved.");
}
