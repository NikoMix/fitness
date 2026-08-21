using Forge.App.Features.Security.ViewModels;

namespace Forge.App.Features.Security;

/// <summary>The screen where the app lock is turned on, tuned, or turned off.</summary>
public partial class AppLockSettingsPage : ContentPage
{
    private readonly AppLockSettingsPageViewModel viewModel;

    /// <summary>Creates the settings screen.</summary>
    /// <param name="viewModel">The screen's view model.</param>
    public AppLockSettingsPage(AppLockSettingsPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Re-probed every time rather than cached. A user can enrol a fingerprint, or remove
        // their passcode, between two visits to this screen, and a stale answer here would
        // offer them a lock the device can no longer honour.
        if (viewModel.RefreshCommand.CanExecute(null))
        {
            viewModel.RefreshCommand.Execute(null);
        }
    }
}
