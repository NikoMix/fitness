using Forge.App.Services.Storage;
using Forge.Core.Abstractions.Data;
using Forge.Infrastructure.Persistence;
using Forge.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using ForgeSecureStorage = Forge.Core.Abstractions.Data.ISecureStorage;

namespace Forge.App.Composition;

/// <summary>
/// Registers the infrastructure that features depend on.
/// </summary>
/// <remarks>
/// Kept separate from feature registration so the two evolve independently: features are added
/// by many people in parallel, whereas infrastructure changes rarely and deserves scrutiny.
/// </remarks>
internal static class InfrastructureRegistration
{
    /// <summary>Registers persistence, storage and platform-backed services.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddForgeInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Startup state is shared for the process lifetime.
        services.AddSingleton<ForgeDatabaseOptions>();
        services.AddSingleton<ForgeStartupService>();

        // The key lives in the Android Keystore or iOS Keychain, never in preferences or source.
        // Aliased because MAUI ships its own ISecureStorage in Microsoft.Maui.Storage.
        services.AddSingleton<ForgeSecureStorage, MauiSecureStorage>();
        services.AddSingleton<IDatabaseKeyProvider, SecureStorageDatabaseKeyProvider>();

        // DbContext is transient, not singleton.
        //
        // MAUI has no per-request scope, so the tempting choice is a singleton. That is wrong
        // here for two reasons: EF's change tracker would grow without bound across the whole
        // session, and DbContext is not thread-safe while Forge reads on background threads
        // during a workout. Transient gives each consumer a short-lived context, which matches
        // how the repositories are written.
        services.AddTransient(provider =>
        {
            var options = provider.GetRequiredService<ForgeDatabaseOptions>();
            return ForgeDbContextFactory.CreateDbContext(options.DatabasePath, options.EncryptionKey);
        });

        // Data access goes through a session, never through a separately resolved repository and
        // unit of work. Registering IRepository<> and IUnitOfWork individually looks convenient
        // but is a trap: both are transient, so each would be handed its own context and a save
        // would commit an empty change tracker while the caller's writes sat on a different one.
        // A session hands out repositories over one shared context, so the failure cannot occur.
        services.AddSingleton<IDataSessionFactory>(provider => new EfDataSessionFactory(() =>
        {
            var options = provider.GetRequiredService<ForgeDatabaseOptions>();
            return ForgeDbContextFactory.CreateDbContext(options.DatabasePath, options.EncryptionKey);
        }));

        return services;
    }
}
