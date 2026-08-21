using Forge.App.Features.Profile;
using Forge.App.Navigation;

namespace Forge.App.Features.Onboarding;

/// <summary>Routes a launch to onboarding only when no persisted profile exists.</summary>
internal sealed class FirstRunGate(ProfileStore profileStore)
{
    /// <summary>Waits for startup and sends returning users directly to the app.</summary>
    public async Task RouteAsync(CancellationToken cancellationToken)
    {
        if (await profileStore.HasProfileAsync(cancellationToken).ConfigureAwait(false))
        {
            await MainThread.InvokeOnMainThreadAsync(() => Microsoft.Maui.Controls.Shell.Current.GoToAsync($"//{ForgeRoutes.Today}"));
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.Welcome));
    }
}
