using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions.Security;

namespace Forge.App.Features.Security.ViewModels;

/// <summary>Backs the lock screen shown when Forge is locked.</summary>
/// <remarks>
/// The copy on this screen is deliberately plain about what the lock is. Telling someone their
/// data is "secured" when a lock screen is a presentation gate over a database whose key the
/// operating system already released is the kind of reassurance that leads to bad decisions,
/// such as leaving an unlocked phone with someone who knows the passcode.
/// </remarks>
public sealed partial class AppLockPageViewModel(AppLockCoordinator coordinator) : ObservableObject
{
    [ObservableProperty]
    private string statusMessage = "Forge is locked.";

    [ObservableProperty]
    private string promptDescription =
        "Use your fingerprint, face or device passcode to continue.";

    [ObservableProperty]
    private bool isBusy;

    /// <summary>Whether the unlock action should be offered.</summary>
    public bool CanUnlock => !IsBusy;

    /// <summary>What the lock does and does not do, stated plainly.</summary>
    public string HonestScopeText { get; } =
        "This asks the device who you are before showing your training and body data. It does "
        + "not add encryption: your data is already encrypted on this device, and anyone who "
        + "knows your device passcode can get past this screen.";

    /// <summary>Reassurance that nothing is at risk while the screen is up.</summary>
    public string NoDataLossText { get; } =
        "Failed attempts never delete anything. If this device can no longer recognise you, "
        + "Forge switches the lock off rather than keeping you out of your own history.";

    /// <summary>Runs an unlock attempt.</summary>
    /// <param name="cancellationToken">Dismisses the prompt.</param>
    /// <returns>A task that completes when the attempt has been handled.</returns>
    [RelayCommand(CanExecute = nameof(CanUnlock))]
    public async Task UnlockAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        UnlockCommand.NotifyCanExecuteChanged();

        try
        {
            var result = await coordinator.UnlockAsync(cancellationToken).ConfigureAwait(true);

            StatusMessage = result.Outcome switch
            {
                AppLockAuthenticationOutcome.Succeeded => "Unlocked.",
                AppLockAuthenticationOutcome.Cancelled => "Forge is still locked. Tap unlock when you are ready.",
                _ => result.Message ?? "Forge is still locked.",
            };
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Forge is still locked. Tap unlock when you are ready.";
        }
        finally
        {
            IsBusy = false;
            UnlockCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanUnlock));
}
