using Forge.App.Features.Settings.ViewModels;

namespace Forge.App.Features.Settings;

public partial class DeleteMyDataPage : ContentPage
{
    private readonly DeleteMyDataPageViewModel viewModel;

    public DeleteMyDataPage(DeleteMyDataPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (viewModel.RefreshCommand.CanExecute(null))
        {
            viewModel.RefreshCommand.Execute(null);
        }
    }
}
