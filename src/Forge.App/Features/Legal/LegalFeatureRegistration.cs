using Forge.App.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Legal;

/// <summary>
/// Dependency registration for the Legal feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Routes belong here too: call <c>Routing.RegisterRoute</c> for any destination this feature
/// owns, using the constants in <c>Forge.App.Navigation.ForgeRoutes</c>.
/// </remarks>
public static class LegalFeatureRegistration
{
    /// <summary>Registers the Legal feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddLegalFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<PrivacyPolicyPage>();
        services.AddTransient<TermsOfServicePage>();
        services.AddTransient<MedicalDisclaimerPage>();
        services.AddTransient<LicencesPage>();

        Routing.RegisterRoute(ForgeRoutes.PrivacyPolicy, typeof(PrivacyPolicyPage));
        Routing.RegisterRoute(ForgeRoutes.TermsOfService, typeof(TermsOfServicePage));
        Routing.RegisterRoute(ForgeRoutes.MedicalDisclaimer, typeof(MedicalDisclaimerPage));
        Routing.RegisterRoute(ForgeRoutes.Licences, typeof(LicencesPage));

        return services;
    }
}
