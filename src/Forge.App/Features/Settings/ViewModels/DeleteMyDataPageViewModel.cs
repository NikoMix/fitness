using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions;
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

    public bool IsConfirmationValid => string.Equals(ConfirmationText.Trim(), ConfirmationWord, StringComparison.Ordinal);

    [RelayCommand]
    private Task ExportBackupAsync()
        => dataErasureService.ExportBackupBeforeErasureAsync(CancellationToken.None);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var preview = await dataErasureService.GetPreviewAsync(CancellationToken.None);
        StorageSummary = $"{FormatBytes(preview.TotalBytes)} found in local app storage and cache.";
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Erasure can fail part-way through and leave files behind, so this cannot claim
            // success. It also must not show ex.Message: the underlying IOException carries file
            // paths and an AggregateException, and this is the screen a user reaches when they
            // have decided to leave.
            await page.DisplayAlertAsync(
                "Some data could not be erased",
                ForgeUserFacingException.DescribeFor(
                    ex,
                    "Forge erased what it could, but some files are still on this device. Close Forge completely, reopen it, and try again. If it keeps failing, uninstalling Forge removes everything it stored."),
                "OK");
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
