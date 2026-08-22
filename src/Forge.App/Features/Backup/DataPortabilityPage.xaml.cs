using Forge.App.Features.Backup.ViewModels;

namespace Forge.App.Features.Backup;

/// <summary>The screen a person uses to obtain a copy of their own data.</summary>
public partial class DataPortabilityPage : ContentPage
{
    private readonly DataPortabilityViewModel viewModel;

    /// <summary>Creates the page.</summary>
    /// <param name="viewModel">The view model driving it.</param>
    public DataPortabilityPage(DataPortabilityViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }
}
