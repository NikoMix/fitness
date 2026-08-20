using Forge.App.Features.Settings.ViewModels;

namespace Forge.App.Features.Settings;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
