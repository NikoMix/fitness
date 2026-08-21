using Forge.App.Navigation;
using Forge.App.Services.Media;
using Forge.App.Features.Media.Library;
using Forge.Core.Abstractions.Media;
using Forge.Infrastructure.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Media;

/// <summary>
/// Dependency registration for the Media feature.
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
public static class MediaFeatureRegistration
{
    /// <summary>Registers the Media feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddMediaFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<HttpClient>();
        services.AddSingleton<IMediaCache>(provider => new FileSystemMediaCache(
            Path.Combine(Microsoft.Maui.Storage.FileSystem.Current.CacheDirectory, "forge-media"),
            provider.GetRequiredService<HttpClient>()));
        services.AddSingleton<IMediaCatalogue, ExerciseMediaCatalogue>();
        services.AddSingleton<IMediaPlaybackPolicy, MauiMediaPlaybackPolicy>();

        // Optional exercise video arrives through the store's own asset delivery, so the install
        // stays small and the user picks the fidelity they want. Platforms without that facility
        // fall back to a service that reports packs as unavailable instead of failing.
#if ANDROID || IOS
        services.AddSingleton<IMediaPackService, PlatformMediaPackService>();
#else
        services.AddSingleton<IMediaPackService, UnavailableMediaPackService>();
#endif

        services.AddTransient<ExerciseVideoViewModel>();
        services.AddTransient<ExerciseVideoPage>();
        services.AddTransient<VideoLibraryViewModel>();
        services.AddTransient<VideoLibraryPage>();

        Routing.RegisterRoute(ForgeRoutes.ExerciseVideo, typeof(ExerciseVideoPage));
        Routing.RegisterRoute(ForgeRoutes.VideoLibrary, typeof(VideoLibraryPage));

        return services;
    }
}
