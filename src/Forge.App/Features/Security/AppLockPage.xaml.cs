using Forge.App.Features.Security.ViewModels;

namespace Forge.App.Features.Security;

/// <summary>
/// The screen shown while Forge is locked.
/// </summary>
/// <remarks>
/// Hardware and gesture back are refused here. Without that, the lock is a page the user can
/// simply dismiss, which would make the whole feature decorative. It is still only a
/// presentation gate - see <c>docs/security/app-lock-threat-model.md</c> for what that does and
/// does not buy.
/// </remarks>
public partial class AppLockPage : ContentPage
{
    private readonly AppLockPageViewModel viewModel;

    /// <summary>Creates the lock screen.</summary>
    /// <param name="viewModel">The screen's view model.</param>
    public AppLockPage(AppLockPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Prompt immediately. Making the user tap "Unlock" before anything happens adds a step
        // to something they will do several times a day, and the platform prompt is itself
        // dismissible, so the button below remains for a second attempt.
        if (viewModel.UnlockCommand.CanExecute(null))
        {
            viewModel.UnlockCommand.Execute(null);
        }
    }

    /// <inheritdoc />
    protected override bool OnBackButtonPressed() => true;
}
