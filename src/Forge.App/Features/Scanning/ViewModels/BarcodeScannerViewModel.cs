using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Scanning.Services;
using Forge.Core.Abstractions;
using Forge.Core.Abstractions.Scanning;
using Forge.Domain.Nutrition.Barcodes;

namespace Forge.App.Features.Scanning.ViewModels;

/// <summary>
/// The barcode scanner screen.
/// </summary>
/// <remarks>
/// <para>
/// Scanning resolves against the food catalogue stored on this device and nothing else. Forge has
/// no backend and calls no food API, so an unrecognised barcode is the ordinary outcome rather
/// than a failure, and the screen treats it that way: it offers to write the label down once and
/// remembers the code for next time.
/// </para>
/// <para>
/// Manual entry is always available, never behind a "camera failed" branch. A scratched barcode, a
/// dark aisle, a phone with no working camera and a refused permission all end in the same place,
/// and that place has to be somewhere a person can finish the job.
/// </para>
/// </remarks>
public sealed partial class BarcodeScannerViewModel : ObservableObject
{
    // Decoders fire continuously while a code is in frame. Anything inside this window that
    // matches what was just handled is the same packet still being pointed at the camera.
    private static readonly TimeSpan RepeatSuppressionWindow = TimeSpan.FromSeconds(2.5);

    private const string CameraLiveMessage = "Point the camera at the barcode.";
    private const string ManualOnlyMessage = "Type the digits under the barcode.";

    private readonly IBarcodeCameraScanner scanner;
    private readonly ICameraPermissionService permissions;
    private readonly IBarcodeCatalogueService catalogue;
    private readonly IBarcodeScanSession session;
    private readonly INavigationService navigation;

    private Barcode? currentBarcode;
    private Guid? matchedFoodId;
    private string? lastHandledKey;
    private DateTimeOffset lastHandledAt;
    private bool hasPromptedThisVisit;
    private bool isSubscribed;

    /// <summary>Initialises the view model.</summary>
    /// <param name="scanner">The camera decoder, which may report itself unsupported.</param>
    /// <param name="permissions">Camera permission.</param>
    /// <param name="catalogue">Local barcode resolution and storage.</param>
    /// <param name="session">The pending scan this screen completes.</param>
    /// <param name="navigation">Navigation, used to close the screen.</param>
    public BarcodeScannerViewModel(
        IBarcodeCameraScanner scanner,
        ICameraPermissionService permissions,
        IBarcodeCatalogueService catalogue,
        IBarcodeScanSession session,
        INavigationService navigation)
    {
        this.scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        this.permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
    }

    /// <summary>Whether a lookup or save is in flight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanRememberBarcode))]
    private bool isBusy;

    /// <summary>The single line of guidance under the viewfinder.</summary>
    [ObservableProperty]
    private string statusMessage = ManualOnlyMessage;

    /// <summary>Whether the camera is running and decoding.</summary>
    [ObservableProperty]
    private bool isCameraLive;

    /// <summary>Whether an explanation is shown in place of the viewfinder.</summary>
    [ObservableProperty]
    private bool showsCameraNotice;

    /// <summary>Headline of the camera explanation.</summary>
    [ObservableProperty]
    private string cameraNoticeHeadline = "Camera scanning is not available";

    /// <summary>Body of the camera explanation.</summary>
    [ObservableProperty]
    private string cameraNoticeMessage = "This build has no barcode camera. Type the digits below instead.";

    /// <summary>Whether asking for camera permission again could still succeed.</summary>
    [ObservableProperty]
    private bool canRequestPermission;

    /// <summary>Whether the system settings page can be opened from here.</summary>
    [ObservableProperty]
    private bool canOpenSettings;

    /// <summary>Whether the active camera exposes a torch.</summary>
    [ObservableProperty]
    private bool isTorchAvailable;

    /// <summary>Whether the torch is lit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TorchButtonText))]
    private bool isTorchOn;

    /// <summary>The digits typed by hand.</summary>
    [ObservableProperty]
    private string manualBarcode = string.Empty;

    /// <summary>Feedback about the typed digits, in plain English.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManualMessage))]
    private string manualMessage = string.Empty;

    /// <summary>Whether a remembered food was found.</summary>
    [ObservableProperty]
    private bool showsMatch;

    /// <summary>The matched food's name.</summary>
    [ObservableProperty]
    private string matchedFoodName = string.Empty;

    /// <summary>The matched food's brand, or an empty string.</summary>
    [ObservableProperty]
    private string matchedFoodBrand = string.Empty;

    /// <summary>A per-100g summary of the matched food, shown so a wrong match is obvious.</summary>
    [ObservableProperty]
    private string matchedNutrition = string.Empty;

    /// <summary>Whether the barcode is valid but not yet known to this device.</summary>
    [ObservableProperty]
    private bool showsUnknown;

    /// <summary>The unknown barcode, shown back exactly as it was scanned or typed.</summary>
    [ObservableProperty]
    private string unknownBarcodeText = string.Empty;

    /// <summary>Name for the food being created.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRememberBarcode))]
    private string newFoodName = string.Empty;

    /// <summary>Optional brand for the food being created.</summary>
    [ObservableProperty]
    private string newFoodBrand = string.Empty;

    /// <summary>Energy per 100 g for the food being created.</summary>
    [ObservableProperty]
    private decimal newFoodEnergyKilocalories;

    /// <summary>Protein per 100 g for the food being created.</summary>
    [ObservableProperty]
    private decimal newFoodProteinGrams;

    /// <summary>Carbohydrate per 100 g for the food being created.</summary>
    [ObservableProperty]
    private decimal newFoodCarbohydrateGrams;

    /// <summary>Fat per 100 g for the food being created.</summary>
    [ObservableProperty]
    private decimal newFoodFatGrams;

    /// <summary>Grams in one serving, or zero when the packet does not say.</summary>
    [ObservableProperty]
    private decimal newFoodServingGrams;

    /// <summary>Feedback about saving the new food.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewFoodMessage))]
    private string newFoodMessage = string.Empty;

    /// <summary>Whether there is manual-entry feedback to show.</summary>
    public bool HasManualMessage => !string.IsNullOrEmpty(ManualMessage);

    /// <summary>Whether there is new-food feedback to show.</summary>
    public bool HasNewFoodMessage => !string.IsNullOrEmpty(NewFoodMessage);

    /// <summary>Whether nothing is in flight, so actions may be taken.</summary>
    /// <remarks>
    /// A property rather than a converter because Forge ships no value converters, and adding one
    /// for a single negation would put a second way of expressing "not busy" into the codebase.
    /// </remarks>
    public bool IsIdle => !IsBusy;

    /// <summary>Whether the new food has enough detail to be saved.</summary>
    public bool CanRememberBarcode => !IsBusy && !string.IsNullOrWhiteSpace(NewFoodName);

    /// <summary>Label for the torch toggle.</summary>
    public string TorchButtonText => IsTorchOn ? "Torch off" : "Torch on";

    /// <summary>Prepares the screen and starts the camera if it can.</summary>
    [RelayCommand]
    private async Task AppearingAsync()
    {
        hasPromptedThisVisit = false;
        ClearResults();
        Subscribe();
        await StartCameraAsync(promptWhenAskable: true);
    }

    /// <summary>Releases the camera when the screen goes away.</summary>
    /// <remarks>
    /// Also completes the pending scan as cancelled. A back gesture leaves no other trace, and a
    /// caller awaiting a result that never arrives would wait forever.
    /// </remarks>
    [RelayCommand]
    private async Task DisappearingAsync()
    {
        Unsubscribe();
        await StopCameraAsync();
        session.Complete(BarcodeScanResult.Cancelled);
    }

    /// <summary>Asks for camera permission after the person chose to be asked.</summary>
    [RelayCommand]
    private async Task RequestPermissionAsync()
    {
        // Only reachable from an explicit tap, so this is never the automatic re-prompt loop that
        // the platform silently swallows.
        hasPromptedThisVisit = true;
        var status = await permissions.RequestAsync(CancellationToken.None);
        await ApplyPermissionStatusAsync(status);
    }

    /// <summary>Opens the system settings page for Forge.</summary>
    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (!await permissions.TryOpenSettingsAsync(CancellationToken.None))
        {
            CameraNoticeMessage = "Forge could not open your system settings. Open Settings, find Forge, "
                + "and allow Camera. Manual entry below works either way.";
        }
    }

    /// <summary>Turns the torch on or off.</summary>
    [RelayCommand]
    private async Task ToggleTorchAsync()
    {
        var wanted = !IsTorchOn;
        IsTorchOn = await scanner.SetTorchAsync(wanted, CancellationToken.None) && wanted;
        IsTorchAvailable = scanner.IsTorchAvailable;
    }

    /// <summary>Looks up the digits typed by hand.</summary>
    [RelayCommand]
    private async Task LookUpManualAsync()
    {
        var parsed = BarcodeNormaliser.Parse(ManualBarcode);
        if (parsed.Barcode is null)
        {
            ManualMessage = Describe(parsed.Reason);
            return;
        }

        ManualMessage = string.Empty;
        await ResolveAsync(parsed.Barcode);
    }

    /// <summary>Returns the matched food to whoever opened the scanner.</summary>
    [RelayCommand]
    private async Task UseMatchedFoodAsync()
    {
        if (currentBarcode is null || matchedFoodId is not { } foodId)
        {
            return;
        }

        session.Complete(BarcodeScanResult.Resolved(foodId, currentBarcode, foodWasCreated: false));
        await navigation.GoBackAsync();
    }

    /// <summary>Creates a food from the typed label and remembers the barcode against it.</summary>
    [RelayCommand]
    private async Task RememberBarcodeAsync()
    {
        if (currentBarcode is not { } barcode)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NewFoodName))
        {
            NewFoodMessage = "Give the food a name so you can find it again.";
            return;
        }

        IsBusy = true;
        try
        {
            var details = new NewFoodDetails(
                NewFoodName,
                NewFoodBrand,
                NewFoodEnergyKilocalories,
                NewFoodProteinGrams,
                NewFoodCarbohydrateGrams,
                NewFoodFatGrams,
                NewFoodServingGrams);

            var foodId = await catalogue.RememberAsync(barcode, details, CancellationToken.None);

            session.Complete(BarcodeScanResult.Resolved(foodId, barcode, foodWasCreated: true));
            await navigation.GoBackAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The local database is the only copy of this, so a failed write has to be reported
            // rather than swallowed: the person needs to know their typing was not saved.
            NewFoodMessage = "Forge could not save that food. Your barcode was not remembered.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Clears the result and goes back to scanning.</summary>
    [RelayCommand]
    private async Task ScanAnotherAsync()
    {
        ClearResults();
        await StartCameraAsync(promptWhenAskable: false);
    }

    /// <summary>Closes the scanner without choosing a food.</summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        session.Complete(BarcodeScanResult.Cancelled);
        await navigation.GoBackAsync();
    }

    private void Subscribe()
    {
        if (isSubscribed)
        {
            return;
        }

        scanner.BarcodeDetected += OnBarcodeDetected;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed)
        {
            return;
        }

        scanner.BarcodeDetected -= OnBarcodeDetected;
        isSubscribed = false;
    }

    private void OnBarcodeDetected(object? sender, BarcodeDetectedEventArgs e)
    {
        // Decoders raise this from a camera thread; everything below touches bindable state.
        MainThread.BeginInvokeOnMainThread(() => _ = HandleDetectionAsync(e));
    }

    private async Task HandleDetectionAsync(BarcodeDetectedEventArgs detection)
    {
        if (IsBusy || ShowsMatch || ShowsUnknown)
        {
            return;
        }

        var parsed = BarcodeNormaliser.Parse(detection.RawValue, detection.Symbology);
        if (parsed.Barcode is not { } barcode)
        {
            // Partial and mis-read frames are constant and are not worth telling anyone about.
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (string.Equals(lastHandledKey, barcode.Gtin14, StringComparison.Ordinal)
            && now - lastHandledAt < RepeatSuppressionWindow)
        {
            return;
        }

        lastHandledKey = barcode.Gtin14;
        lastHandledAt = now;

        await ResolveAsync(barcode);
    }

    private async Task ResolveAsync(Barcode barcode)
    {
        IsBusy = true;
        try
        {
            // Free the camera while a result card is on screen. Leaving it decoding behind a card
            // burns battery and fires detections at a screen that is no longer looking for them.
            await StopCameraAsync();

            currentBarcode = barcode;
            var resolution = await catalogue.ResolveAsync(barcode, CancellationToken.None);

            if (resolution is { IsKnown: true, FoodItemId: { } foodId })
            {
                matchedFoodId = foodId;
                MatchedFoodName = resolution.FoodName ?? string.Empty;
                MatchedFoodBrand = resolution.Brand ?? string.Empty;
                MatchedNutrition = resolution.NutritionSummary ?? string.Empty;
                ShowsMatch = true;
                StatusMessage = "Barcode recognised.";
                return;
            }

            matchedFoodId = null;
            UnknownBarcodeText = barcode.ScannedValue;
            NewFoodMessage = string.Empty;
            ShowsUnknown = true;
            StatusMessage = "New barcode. Add it once and Forge will know it next time.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = "Forge could not read your local food data just now. Try again in a moment.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartCameraAsync(bool promptWhenAskable)
    {
        if (!scanner.IsSupported)
        {
            ShowNotice(
                "Camera scanning is not available",
                "This build of Forge has no barcode camera. Type the digits printed under the barcode "
                + "and Forge will look them up locally.");
            CanRequestPermission = false;
            return;
        }

        var status = await permissions.CheckAsync(CancellationToken.None);

        if (status == CameraPermissionStatus.Denied && promptWhenAskable && !hasPromptedThisVisit)
        {
            hasPromptedThisVisit = true;
            status = await permissions.RequestAsync(CancellationToken.None);
        }

        await ApplyPermissionStatusAsync(status);
    }

    private async Task ApplyPermissionStatusAsync(CameraPermissionStatus status)
    {
        switch (status)
        {
            case CameraPermissionStatus.Granted:
                await BeginScanningAsync();
                return;

            case CameraPermissionStatus.Denied:
                ShowNotice(
                    "Camera access is off",
                    "Forge only uses the camera to read a barcode, and the image never leaves your phone. "
                    + "You can allow it now, or just type the digits below.");
                CanRequestPermission = true;
                return;

            case CameraPermissionStatus.PermanentlyDenied:
                // A settled decision, not a problem to nag about. Say where it can be changed once
                // and then get out of the way.
                ShowNotice(
                    "Camera access is turned off",
                    "Forge cannot ask again from here. You can allow Camera for Forge in your system "
                    + "settings, or keep using manual entry, which works just as well.");
                CanRequestPermission = false;
                CanOpenSettings = permissions.CanOpenSettings;
                return;

            default:
                ShowNotice(
                    "No camera available",
                    "Forge could not find a usable camera on this device. Type the digits printed under "
                    + "the barcode instead.");
                CanRequestPermission = false;
                return;
        }
    }

    private async Task BeginScanningAsync()
    {
        var started = await scanner.StartAsync(CancellationToken.None);
        if (started == CameraScanStartResult.Started)
        {
            IsCameraLive = true;
            ShowsCameraNotice = false;
            IsTorchAvailable = scanner.IsTorchAvailable;
            IsTorchOn = scanner.IsTorchOn;
            StatusMessage = CameraLiveMessage;
            return;
        }

        ShowNotice(
            "The camera could not start",
            started == CameraScanStartResult.PermissionDenied
                ? "Camera access was withdrawn. Type the digits below, or allow Camera for Forge in your system settings."
                : "Another app may be using the camera. Type the digits printed under the barcode instead.");
        CanRequestPermission = started == CameraScanStartResult.PermissionDenied;
        CanOpenSettings = CanRequestPermission && permissions.CanOpenSettings;
    }

    private async Task StopCameraAsync()
    {
        await scanner.StopAsync(CancellationToken.None);
        IsCameraLive = false;
        IsTorchOn = false;
        IsTorchAvailable = false;
    }

    private void ShowNotice(string headline, string message)
    {
        IsCameraLive = false;
        IsTorchAvailable = false;
        IsTorchOn = false;
        ShowsCameraNotice = true;
        CameraNoticeHeadline = headline;
        CameraNoticeMessage = message;
        StatusMessage = ManualOnlyMessage;

        // Each notice re-enables only the routes that apply to it. Offering "open settings" when
        // this build simply has no decoder would send someone to a screen with nothing to change.
        CanRequestPermission = false;
        CanOpenSettings = false;
    }

    private void ClearResults()
    {
        ShowsMatch = false;
        ShowsUnknown = false;
        matchedFoodId = null;
        currentBarcode = null;
        MatchedFoodName = string.Empty;
        MatchedFoodBrand = string.Empty;
        MatchedNutrition = string.Empty;
        UnknownBarcodeText = string.Empty;
        NewFoodName = string.Empty;
        NewFoodBrand = string.Empty;
        NewFoodEnergyKilocalories = 0m;
        NewFoodProteinGrams = 0m;
        NewFoodCarbohydrateGrams = 0m;
        NewFoodFatGrams = 0m;
        NewFoodServingGrams = 0m;
        NewFoodMessage = string.Empty;
        ManualMessage = string.Empty;
        ManualBarcode = string.Empty;
        lastHandledKey = null;
    }

    private static string Describe(BarcodeRejectionReason reason) => reason switch
    {
        BarcodeRejectionReason.Empty => "Type the digits printed under the barcode.",
        BarcodeRejectionReason.NotAllDigits => "A barcode is digits only. Spaces and dashes are fine, letters are not.",
        BarcodeRejectionReason.UnsupportedLength => "That is not a length Forge recognises. Food barcodes have 8, 12 or 13 digits.",
        BarcodeRejectionReason.CheckDigitMismatch => "Those digits do not add up, so one of them is probably wrong. Worth another look.",
        BarcodeRejectionReason.UnsupportedNumberSystem => "That eight-digit code is not one Forge can expand. Try the longer code if the packet has one.",
        _ => string.Format(CultureInfo.CurrentCulture, "Forge could not read that barcode ({0}).", reason),
    };
}
