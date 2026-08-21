using Forge.App.Navigation;
using Forge.App.Services.Media;
using Forge.App.Features.Media.Library;
using Forge.Core.Abstractions.Media;
using Forge.Infrastructure.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
public static partial class MediaFeatureRegistration
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
        services.AddSingleton<IMediaPackService>(provider => CreatePlatformPackService(
            provider.GetRequiredService<ILogger<PlatformMediaPackService>>()));
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

#if ANDROID || IOS
    /// <summary>
    /// Builds the platform pack service, degrading to "no video" if the store binding will not load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asset delivery bindings resolve native classes when the service is constructed, and a
    /// mismatch between the binding and the version of the store library on the device surfaces as
    /// a <see cref="TypeLoadException"/> or a Java linkage error rather than a return value. That
    /// is currently reproducible on Android: the generated <c>PackStateListener</c> overrides
    /// <c>onStateUpdate</c>, which is final in the Play Core class it derives from, and the JVM
    /// rejects the class the moment it is loaded.
    /// </para>
    /// <para>
    /// Video is explicitly optional in Forge and every exercise is written to be followable from
    /// text alone, so an unusable delivery binding must cost the user the video and nothing else.
    /// Before this guard it took the whole process down as soon as anything touched an exercise
    /// that might have a demonstration, which on a tablet is the library itself.
    /// </para>
    /// </remarks>
    /// <param name="logger">Records why delivery was disabled.</param>
    /// <returns>The platform service, or the unavailable fallback.</returns>
    private static IMediaPackService CreatePlatformPackService(ILogger<PlatformMediaPackService> logger)
    {
        try
        {
            return new PlatformMediaPackService();
        }
        catch (Exception ex)
        {
            LogDeliveryUnavailable(logger, ex);
            return new UnavailableMediaPackService();
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Store asset delivery is unavailable; optional video packs are disabled.")]
    private static partial void LogDeliveryUnavailable(ILogger logger, Exception exception);
#endif
}
