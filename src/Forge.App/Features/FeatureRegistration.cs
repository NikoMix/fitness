using Forge.App.Features.Backup;
using Forge.App.Features.Coaching;
using Forge.App.Features.Engagement;
using Forge.App.Features.Exercises;
using Forge.App.Features.Health;
using Forge.App.Features.Hydration;
using Forge.App.Features.Insights;
using Forge.App.Features.Legal;
using Forge.App.Features.Media;
using Forge.App.Features.Nutrition;
using Forge.App.Features.Onboarding;
using Forge.App.Features.Plans;
using Forge.App.Features.Profile;
using Forge.App.Features.Progress;
using Forge.App.Features.Scanning;
using Forge.App.Features.Security;
using Forge.App.Features.Settings.Localization;
using Forge.App.Features.Settings;
using Forge.App.Features.Shop;
using Forge.App.Features.Today;
using Forge.App.Features.Train;
using Forge.App.Features.Workout;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features;

/// <summary>
/// The single ordered list of feature registrations.
/// </summary>
/// <remarks>
/// <para>
/// This file exists purely to keep the shared merge surface small. Every feature owns its own
/// <c>Add&lt;Name&gt;Feature</c> method inside its own folder, and this file only calls them.
/// Adding a feature therefore means creating one folder and adding one line here, rather than
/// editing a growing registration block in <c>MauiProgram</c> that every branch touches.
/// </para>
/// <para>
/// Keep the list alphabetical. A stable order makes a concurrent edit a one-line conflict that
/// resolves by taking both sides, instead of a tangle.
/// </para>
/// </remarks>
public static class FeatureRegistration
{
    /// <summary>Registers every Forge feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddForgeFeatures(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddBackupFeature()
            .AddCoachingFeature()
            .AddEngagementFeature()
            .AddExercisesFeature()
            .AddHealthFeature()
            .AddHydrationFeature()
            .AddInsightsFeature()
            .AddLegalFeature()
            .AddLocalizationFeature()
            .AddMediaFeature()
            .AddNutritionFeature()
            .AddOnboardingFeature()
            .AddPlansFeature()
            .AddProfileFeature()
            .AddProgressFeature()
            .AddScanningFeature()
            .AddSecurityFeature()
            .AddSettingsFeature()
            .AddShopFeature()
            .AddTodayFeature()
            .AddTrainFeature()
            .AddWorkoutFeature();
    }
}
