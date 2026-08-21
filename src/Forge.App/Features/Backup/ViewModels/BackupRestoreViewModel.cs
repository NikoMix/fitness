using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions.Backup;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Forge.App.Features.Backup.ViewModels;

public sealed partial class BackupRestoreViewModel(IBackupService backupService) : ObservableObject
{
    [ObservableProperty]
    private string status = "Create a portable backup before changing devices or uninstalling Forge.";

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<BackupListItemViewModel> Backups { get; } = [];

    [RelayCommand]
    private async Task LoadBackupsAsync()
    {
        Backups.Clear();
        var directory = BackupDirectory;
        var backups = await backupService.ListBackupsAsync(directory, CancellationToken.None);
        foreach (var backup in backups)
        {
            Backups.Add(new BackupListItemViewModel(backup.FilePath, backup.Manifest.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), FormatCounts(backup.Manifest.RecordCounts), FormatBytes(backup.LengthBytes)));
        }

        Status = Backups.Count == 0 ? "No app-local backups yet. Create one and keep a copy outside the device." : $"{Backups.Count} app-local backup(s) available.";
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var progressReporter = new Progress<BackupProgress>(UpdateProgress);
            var result = await backupService.CreateBackupAsync(BackupDirectory, progressReporter, CancellationToken.None);
            await using var stream = File.OpenRead(result.FilePath);
            var saveResult = await FileSaver.Default.SaveAsync(Path.GetFileName(result.FilePath), stream, CancellationToken.None);
            Status = saveResult.IsSuccessful
                ? "Backup verified and saved. Keep this file somewhere safe outside Forge."
                : "Backup was created locally, but saving a copy was cancelled.";
            await LoadBackupsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PickAndRestoreAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a Forge backup" });
        if (picked is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var verification = await backupService.VerifyBackupAsync(picked.FullPath, CancellationToken.None);
            if (!verification.IsValid)
            {
                Status = verification.Message;
                return;
            }

            var page = global::Microsoft.Maui.Controls.Shell.Current.CurrentPage;
            var confirmed = page is not null && await page.DisplayAlertAsync(
                "Overwrite local data?",
                "Restoring this backup replaces all current Forge data on this device. If the restore fails, existing data is left unchanged.",
                "Restore backup",
                "Cancel");
            if (!confirmed)
            {
                Status = "Restore cancelled.";
                return;
            }

            var result = await backupService.RestoreBackupAsync(picked.FullPath, new Progress<BackupProgress>(UpdateProgress), CancellationToken.None);
            Status = result.Message;
            await LoadBackupsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BackupDirectory => Path.Combine(FileSystem.AppDataDirectory, "Backups");

    private void UpdateProgress(BackupProgress update)
    {
        Status = update.Message;
        Progress = update.PercentComplete / 100d;
    }

    private static string FormatCounts(IReadOnlyDictionary<string, int> counts) => string.Join(" · ", counts.Where(static pair => pair.Value > 0).Select(static pair => $"{pair.Key}: {pair.Value}"));

    private static string FormatBytes(long bytes)
    {
        var value = bytes / 1024d;
        return value < 1024 ? $"{value:N1} KB" : $"{value / 1024:N1} MB";
    }
}

public sealed record BackupListItemViewModel(string FilePath, string Created, string RecordCounts, string Size);

