using Microsoft.Extensions.DependencyInjection;
using Forge.App.Hosting;
using Forge.App.Navigation;
using Forge.App.Features.Profile;

namespace Forge.App.Features.Onboarding;

/// <summary>
/// Dependency registration for the Onboarding feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Routes belong here too: call <c>Routing.RegisterRoute</c> for any destination this feature
/// owns, using the constants in <c>Forge.App.Navigation.ForgeRoutes</c>.
/// </remarks>
public static class OnboardingFeatureRegistration
{
    /// <summary>Registers the Onboarding feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOnboardingFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<WelcomePage>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<GoalWizardPage>();
        services.AddTransient<GoalWizardViewModel>();
        services.AddSingleton<IOnboardingDraftStore, OnboardingDraftStore>();
        services.AddSingleton<FirstRunGate>();
        services.AddSingleton(provider =>
        {
            var shell = new AppShell(provider.GetRequiredService<AppShellViewModel>());
            var hasRun = false;
            shell.Loaded += async (_, _) =>
            {
                if (hasRun)
                {
                    return;
                }

                hasRun = true;
                try
                {
                    await provider.GetRequiredService<FirstRunGate>().RouteAsync(CancellationToken.None);
                }
                catch (InvalidOperationException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"First-run routing could not read the local profile: {ex}");
                }
            };

            return shell;
        });

        Routing.RegisterRoute(ForgeRoutes.Welcome, typeof(WelcomePage));
        Routing.RegisterRoute(ForgeRoutes.GoalWizard, typeof(GoalWizardPage));

        return services;
    }
}
