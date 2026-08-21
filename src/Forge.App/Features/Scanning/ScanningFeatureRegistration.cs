using Forge.App.Features.Scanning.Services;
using Forge.App.Features.Scanning.ViewModels;
using Forge.App.Navigation;
using Forge.Core.Abstractions.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Scanning;

/// <summary>
/// Dependency registration for the Scanning feature.
/// </summary>
/// <remarks>
/// <para>
/// Add one line to <c>FeatureRegistration.AddForgeFeatures</c> to switch this on:
/// <c>.AddScanningFeature()</c>.
/// </para>
/// <para>
/// The scanner is reached through <see cref="IBarcodeScanCoordinator"/> rather than by navigating
/// to the route directly, because the caller needs the result and Shell navigation is one-way.
/// </para>
/// </remarks>
public static class ScanningFeatureRegistration
{
    /// <summary>Registers the Scanning feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddScanningFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Forge references no barcode decoding package, so this is the only scanner that ships: it
        // reports itself unsupported and the screen runs on manual entry.
        //
        // To add real decoding, take ZXing.Net.Maui (the fallback sanctioned in
        // docs/architecture/overview.md). Its CameraBarcodeReaderView is both the camera surface
        // and the decoder, so one class should implement IBarcodeCameraScanner and
        // IBarcodeCameraPreviewFactory over a single view instance and be registered for both
        // below. Nothing else in this feature changes.
        services.AddSingleton<IBarcodeCameraScanner, UnavailableBarcodeCameraScanner>();
        services.AddSingleton<IBarcodeCameraPreviewFactory, NoBarcodeCameraPreviewFactory>();

        services.AddSingleton<ICameraPermissionService, MauiCameraPermissionService>();
        services.AddTransient<IBarcodeCatalogueService, BarcodeCatalogueService>();

        // One coordinator instance behind both roles: the caller-facing one that opens a scan and
        // the scanner-facing one that completes it. Two instances would mean the page completing a
        // scan nobody is waiting on.
        services.AddSingleton<BarcodeScanCoordinator>();
        services.AddSingleton<IBarcodeScanCoordinator>(provider => provider.GetRequiredService<BarcodeScanCoordinator>());
        services.AddSingleton<IBarcodeScanSession>(provider => provider.GetRequiredService<BarcodeScanCoordinator>());

        services.AddTransient<BarcodeScannerViewModel>();
        services.AddTransient<BarcodeScannerPage>();
        Routing.RegisterRoute(ForgeRoutes.BarcodeScanner, typeof(BarcodeScannerPage));

        return services;
    }
}
