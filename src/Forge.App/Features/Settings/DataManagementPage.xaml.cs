using Forge.App.Features.Settings.ViewModels;

namespace Forge.App.Features.Settings;

public partial class DataManagementPage : ContentPage
{
    private readonly DataManagementPageViewModel viewModel;

    public DataManagementPage(DataManagementPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (viewModel.RefreshStorageCommand.CanExecute(null))
        {
            viewModel.RefreshStorageCommand.Execute(null);
        }
    }
}
