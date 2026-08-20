using Forge.App.Features.Settings.ViewModels;

namespace Forge.App.Features.Settings;

public partial class NotificationSettingsPage : ContentPage
{
    public NotificationSettingsPage(NotificationSettingsPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
