using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Core.Abstractions.Preferences;

namespace Forge.App.Features.Settings.ViewModels;

public sealed partial class DataManagementPageViewModel(IDataErasureService dataErasureService) : ObservableObject
{
    [ObservableProperty]
    private string storageUsage = "Calculating…";

    [ObservableProperty]
    private string backupStatus = "Backup and restore are owned by Epic E26. Export before deletion is exposed here as the wiring point.";

    [RelayCommand]
    private async Task RefreshStorageAsync()
    {
        var preview = await dataErasureService.GetPreviewAsync(CancellationToken.None);
        StorageUsage = FormatBytes(preview.TotalBytes);
    }

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "CommunityToolkit command generation binds instance commands from XAML.")]
    private Task OpenDeleteMyDataAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.DeleteMyData);

    [RelayCommand]
    private Task ExportBackupAsync()
        => dataErasureService.ExportBackupBeforeErasureAsync(CancellationToken.None);

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

        return $"{value:N1} {units[unitIndex]} stored locally";
    }
}
