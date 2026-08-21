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
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = viewModel.LoadAsync();
    }
}
