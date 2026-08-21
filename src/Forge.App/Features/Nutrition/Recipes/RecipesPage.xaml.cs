using System.ComponentModel;
using Forge.App.Adaptive;

namespace Forge.App.Features.Nutrition.Recipes;

/// <summary>Offline recipe list and detail page.</summary>
public partial class RecipesPage : ContentPage
{
    private readonly RecipesViewModel viewModel;

    /// <summary>Creates the page.</summary>
    public RecipesPage(RecipesViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;

        // Whether the list stays visible behind a chosen recipe depends on the measured width, not
        // on the device, so the view model is told rather than left to guess.
        Adaptive.PropertyChanged += OnAdaptiveLayoutChanged;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.IsSplitLayout = Adaptive.IsSplit;
        _ = viewModel.LoadAsync();
    }

    private void OnAdaptiveLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdaptiveHost.IsSplit))
        {
            viewModel.IsSplitLayout = Adaptive.IsSplit;
        }
    }
}
