using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions.Preferences;

namespace Forge.App.Features.Settings.ViewModels;

public sealed partial class DeleteMyDataPageViewModel(IDataErasureService dataErasureService) : ObservableObject
{
    private const string ConfirmationWord = "DELETE";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EraseAllDataCommand))]
    [NotifyPropertyChangedFor(nameof(IsConfirmationValid))]
    private string confirmationText = string.Empty;

    [ObservableProperty]
    private string storageSummary = "Calculating local storage…";

    [ObservableProperty]
    private string wiringStatus = "Checking erasure implementation…";

    public bool IsConfirmationValid => string.Equals(ConfirmationText.Trim(), ConfirmationWord, StringComparison.Ordinal);

    [RelayCommand]
    private Task ExportBackupAsync()
        => dataErasureService.ExportBackupBeforeErasureAsync(CancellationToken.None);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var preview = await dataErasureService.GetPreviewAsync(CancellationToken.None);
        StorageSummary = $"{FormatBytes(preview.TotalBytes)} found in local app storage and cache.";
        WiringStatus = preview.PersistenceImplementationWired
            ? "The persistence erasure service is wired."
            : "Wiring point: persistence must replace the placeholder IDataErasureService before release.";
    }

    [RelayCommand(CanExecute = nameof(IsConfirmationValid))]
    private async Task EraseAllDataAsync()
    {
        var page = Microsoft.Maui.Controls.Shell.Current;
        var confirmed = await page.DisplayAlertAsync(
            "Erase all Forge data?",
            "This is irreversible. Because Forge has no cloud backup, erased data cannot be recovered.",
            "Erase permanently",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await dataErasureService.EraseAllLocalDataAsync(CancellationToken.None);
            await page.DisplayAlertAsync("Data erased", "All local Forge data has been erased.", "OK");
        }
        catch (NotSupportedException ex)
        {
            await page.DisplayAlertAsync("Erasure not wired", ex.Message, "OK");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:N1} {units[unitIndex]}";
    }
}
