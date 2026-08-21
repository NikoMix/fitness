using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Settings.Services;
using Forge.App.Navigation;

namespace Forge.App.Features.Settings.ViewModels;

public sealed partial class DataManagementPageViewModel(IStorageUsageService storageUsageService) : ObservableObject
{
    [ObservableProperty]
    private string storageUsage = "Calculating…";

    [ObservableProperty]
    private string backupStatus = "Encrypted local backup can be exported and restored. Open-format export and competitor import are also available.";

    [RelayCommand]
    private async Task RefreshStorageAsync()
    {
        var usage = await storageUsageService.GetUsageAsync(CancellationToken.None);
        StorageUsage = $"Database: {FormatBytes(usage.DatabaseBytes)} · Downloaded media: {FormatBytes(usage.DownloadedMediaBytes)} · Reclaimable: {FormatBytes(usage.ReclaimableMediaBytes)}";
    }

    [RelayCommand]
    private async Task ReclaimMediaAsync()
    {
        var reclaimedBytes = await storageUsageService.ReclaimDownloadedMediaAsync(CancellationToken.None);
        StorageUsage = $"Reclaimed {FormatBytes(reclaimedBytes)} from downloaded media.";
    }

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "CommunityToolkit command generation binds instance commands from XAML.")]
    private Task OpenDeleteMyDataAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.DeleteMyData);

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "CommunityToolkit command generation binds instance commands from XAML.")]
    private Task OpenBackupRestoreAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.BackupRestore);

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "CommunityToolkit command generation binds instance commands from XAML.")]
    private Task OpenExportDataAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.ExportData);

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "CommunityToolkit command generation binds instance commands from XAML.")]
    private Task OpenImportDataAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.ImportData);

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
