using Forge.App.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Plans;

/// <summary>Dependency registration for the Plans feature.</summary>
public static class PlansFeatureRegistration
{
    /// <summary>Registers the Plans feature.</summary>
    public static IServiceCollection AddPlansFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IPlanPersistenceService, PlanPersistenceService>();
        services.AddTransient<PlanListViewModel>();
        services.AddTransient<PlanListPage>();
        services.AddTransient<PlanTemplatesViewModel>();
        services.AddTransient<PlanTemplatesPage>();
        services.AddTransient<PlanEditorViewModel>();
        services.AddTransient<PlanEditorPage>();
        services.AddTransient<PlanScheduleViewModel>();
        services.AddTransient<PlanSchedulePage>();

        Routing.RegisterRoute(ForgeRoutes.PlanList, typeof(PlanListPage));
        Routing.RegisterRoute(ForgeRoutes.PlanTemplates, typeof(PlanTemplatesPage));
        Routing.RegisterRoute(ForgeRoutes.PlanEditor, typeof(PlanEditorPage));
        Routing.RegisterRoute(ForgeRoutes.PlanSchedule, typeof(PlanSchedulePage));

        return services;
    }
}
