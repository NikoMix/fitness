using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Profile;
using Forge.App.Navigation;

namespace Forge.App.Features.Onboarding;

/// <summary>View model for the first-run welcome screen.</summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly ProfileStore? profileStore;

    public WelcomeViewModel()
    {
    }

    public WelcomeViewModel(ProfileStore profileStore)
    {
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
    }

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool hasError;

    /// <summary>Starts the guided goal wizard.</summary>
    [RelayCommand]
    private static Task StartAsync() => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.GoalWizard);

    /// <summary>Skips onboarding so the app is immediately usable without credentials.</summary>
    [RelayCommand]
    private async Task SkipAsync()
    {
        if (profileStore is null)
        {
            await Microsoft.Maui.Controls.Shell.Current.GoToAsync($"//{ForgeRoutes.Today}");
            return;
        }

        try
        {
            IsBusy = true;
            HasError = false;
            await profileStore.EnsureDefaultProfileAsync(CancellationToken.None);
            await Microsoft.Maui.Controls.Shell.Current.GoToAsync($"//{ForgeRoutes.Today}");
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
