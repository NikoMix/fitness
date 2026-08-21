using Forge.App.Features.Scanning.Services;
using Forge.App.Features.Scanning.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Scanning;

/// <summary>
/// Barcode scanner page.
/// </summary>
/// <remarks>
/// Hosts whatever preview the current build's decoder supplies. When there is none - which is the
/// case today - the viewfinder card stays hidden and the screen runs on manual entry, which is a
/// path the person needs anyway when a barcode is damaged or the light is poor.
/// </remarks>
public partial class BarcodeScannerPage : ContentPage
{
    // The preview is a fixed block of screen rather than a proportion, because a viewfinder that
    // resizes as cards appear and disappear below it makes a code impossible to hold in frame.
    // Expressed in touch targets so it stays on the shared scale instead of being a magic number.
    private const double PreviewHeightInTouchTargets = 4d;
    private const double FallbackTouchTarget = 64d;

    private readonly BarcodeScannerViewModel viewModel;

    /// <summary>Initialises the page.</summary>
    public BarcodeScannerPage()
        : this(ResolveViewModel(), ResolvePreviewFactory())
    {
    }

    /// <summary>Initialises the page.</summary>
    /// <param name="viewModel">The scanner view model.</param>
    /// <param name="previewFactory">Supplies the camera preview, when this build has one.</param>
    public BarcodeScannerPage(BarcodeScannerViewModel viewModel, IBarcodeCameraPreviewFactory previewFactory)
    {
        ArgumentNullException.ThrowIfNull(previewFactory);

        InitializeComponent();
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;

        PreviewHost.HeightRequest = Token("TouchTargetPrimary", FallbackTouchTarget) * PreviewHeightInTouchTargets;
        PreviewHost.Content = previewFactory.CreatePreview();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.AppearingCommand.Execute(null);
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        // Releases the camera and, if nothing else has, completes the pending scan as cancelled so
        // a caller awaiting a result is never left hanging by a back gesture.
        viewModel.DisappearingCommand.Execute(null);
        base.OnDisappearing();
    }

    private static BarcodeScannerViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<BarcodeScannerViewModel>()
        ?? throw new InvalidOperationException("The barcode scanner view model could not be resolved.");

    private static IBarcodeCameraPreviewFactory ResolvePreviewFactory() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<IBarcodeCameraPreviewFactory>()
        ?? throw new InvalidOperationException("The barcode camera preview factory could not be resolved.");

    private static double Token(string key, double fallback)
    {
        if (Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var value) == true
            && value is double token)
        {
            return token;
        }

        return fallback;
    }
}
