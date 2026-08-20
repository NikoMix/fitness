using Forge.App.Features.Settings.ViewModels;

namespace Forge.App.Features.Settings;

public partial class UnitsSettingsPage : ContentPage
{
    public UnitsSettingsPage(UnitsSettingsPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
