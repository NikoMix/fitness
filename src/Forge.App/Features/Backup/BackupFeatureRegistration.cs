using Forge.App.Features.Backup.ViewModels;
using Forge.App.Navigation;
using Forge.Core.Abstractions.Backup;
using Forge.Infrastructure.Backup;
using Forge.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;

namespace Forge.App.Features.Backup;

/// <summary>
/// Dependency registration for the Backup feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Routes belong here too: call <c>Routing.RegisterRoute</c> for any destination this feature
/// owns, using the constants in <c>Forge.App.Navigation.ForgeRoutes</c>. Every routed page
/// must also be registered here, or navigating to it throws at runtime - CI enforces the
/// pairing via tools/ci/Test-RouteRegistrations.ps1.
/// </remarks>
public static class BackupFeatureRegistration
{
    /// <summary>Registers the Backup feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddBackupFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IBackupService, ForgeBackupService>();
        services.AddTransient<IDataExporter, ForgeDataExporter>();
        services.AddTransient<IDataImporter, ForgeDataImporter>();

        services.AddTransient<BackupRestoreViewModel>();
        services.AddTransient<ExportDataViewModel>();
        services.AddTransient<ImportDataViewModel>();

        services.AddTransient<BackupRestorePage>();
        services.AddTransient<ExportDataPage>();
        services.AddTransient<ImportDataPage>();

        Routing.RegisterRoute(ForgeRoutes.BackupRestore, typeof(BackupRestorePage));
        Routing.RegisterRoute(ForgeRoutes.ExportData, typeof(ExportDataPage));
        Routing.RegisterRoute(ForgeRoutes.ImportData, typeof(ImportDataPage));

        return services;
    }
}

