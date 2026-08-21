using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Profile;
using Forge.App.Navigation;

namespace Forge.App.Features.Onboarding;

/// <summary>
/// View model for the first-run welcome screen.
/// </summary>
/// <remarks>
/// Skipping stays available and stays honest. It is a real choice with real consequences, so this
/// screen says what skipping costs rather than presenting the two paths as equivalent, and Today
/// and Profile then offer setup back as a specific named list of what is missing.
/// </remarks>
public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly ProfileStore? profileStore;
    private readonly IOnboardingDraftStore? draftStore;

    /// <summary>Initialises an instance with no persistence, used by the XAML designer.</summary>
    public WelcomeViewModel()
    {
    }

    /// <summary>Initialises the view model.</summary>
    /// <param name="profileStore">Persistence for the minimal skip profile.</param>
    /// <param name="draftStore">Persistence for a partially completed setup.</param>
    public WelcomeViewModel(ProfileStore profileStore, IOnboardingDraftStore draftStore)
    {
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        this.draftStore = draftStore ?? throw new ArgumentNullException(nameof(draftStore));
    }

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private bool hasSavedDraft;

    [ObservableProperty]
    private string startActionText = "Set up my goal";

    /// <summary>Checks whether an interrupted setup is waiting to be resumed.</summary>
    public void Refresh()
    {
        HasSavedDraft = draftStore?.HasDraft() ?? false;
        StartActionText = HasSavedDraft ? "Pick up where you left off" : "Set up my goal";
    }

    /// <summary>Starts, or resumes, the guided goal wizard.</summary>
    [RelayCommand]
    private static Task StartAsync() => Shell.Current.GoToAsync(ForgeRoutes.GoalWizard);

    /// <summary>Skips onboarding so the app is immediately usable without credentials.</summary>
    [RelayCommand]
    private async Task SkipAsync()
    {
        if (profileStore is null)
        {
            await Shell.Current.GoToAsync($"//{ForgeRoutes.Today}").ConfigureAwait(true);
            return;
        }

        try
        {
            IsBusy = true;
            HasError = false;
            await profileStore.EnsureDefaultProfileAsync(CancellationToken.None).ConfigureAwait(true);
            await Shell.Current.GoToAsync($"//{ForgeRoutes.Today}").ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
            ErrorMessage = "Forge is still preparing local storage. Please try again in a moment.";
            HasError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
