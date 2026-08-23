using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Settings.Services;
using Forge.App.Navigation;
using Forge.Core.Abstractions;
using Forge.Core.Abstractions.Diagnostics;

namespace Forge.App.Features.Settings.ViewModels;

public sealed partial class DataManagementPageViewModel(
    IStorageUsageService storageUsageService,
    IDiagnosticLog diagnosticLog) : ObservableObject
{
    [ObservableProperty]
    private string storageUsage = "Calculating…";

    [ObservableProperty]
    private string backupStatus = "Encrypted local backup can be exported and restored. Open-format export and competitor import are also available.";

    /// <summary>What the diagnostic log currently holds, or why there is nothing to send.</summary>
    [ObservableProperty]
    private string diagnosticStatus = "Checking…";

    /// <summary>
    /// Set when a previous launch ended in a crash.
    /// </summary>
    /// <remarks>
    /// This is the part of the crash boundary a user can see. The process still dies - nothing can
    /// prevent that once an exception reaches the top of the UI thread - but the launch after it
    /// says so plainly instead of leaving somebody to wonder whether they imagined it.
    /// </remarks>
    [ObservableProperty]
    private bool hasRecentCrash;

    /// <summary>The sentence shown when a previous launch crashed.</summary>
    [ObservableProperty]
    private string recentCrashSummary = string.Empty;

    [RelayCommand]
    private async Task RefreshStorageAsync()
    {
        var usage = await storageUsageService.GetUsageAsync(CancellationToken.None);
        StorageUsage = $"Database: {FormatBytes(usage.DatabaseBytes)} · Downloaded media: {FormatBytes(usage.DownloadedMediaBytes)} · Reclaimable: {FormatBytes(usage.ReclaimableMediaBytes)}";
        await RefreshDiagnosticsAsync();
    }

    [RelayCommand]
    private async Task RefreshDiagnosticsAsync()
    {
        try
        {
            var summary = await diagnosticLog.GetSummaryAsync(CancellationToken.None);

            DiagnosticStatus = summary switch
            {
                { IsWritable: false } => "Forge could not write a diagnostic log on this device. There may be no space left.",
                { HasContent: false } => "Nothing has been recorded yet. Forge only writes here when something goes wrong.",
                _ => $"{FormatSize(summary.TotalBytes)} across {summary.FileCount} file{(summary.FileCount == 1 ? string.Empty : "s")}, of at most {FormatSize(summary.BudgetBytes)}.",
            };

            if (summary.LastCrash is { } crash)
            {
                HasRecentCrash = true;

                // The type name, never the message. An exception's message is the single most
                // likely place for something a user typed to end up on screen, and this screen has
                // form: the workout summary once showed a LINQ expression, a parameter name and a
                // Microsoft support URL to somebody who had just finished training.
                RecentCrashSummary = $"Forge closed unexpectedly on {crash.OccurredAt.ToLocalTime():d MMMM} at {crash.OccurredAt.ToLocalTime():HH:mm}. Nothing you had saved was lost.";
            }
            else
            {
                HasRecentCrash = false;
                RecentCrashSummary = string.Empty;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticStatus = ForgeUserFacingException.DescribeFor(ex, "Forge could not read its diagnostic log.");
        }
    }

    [RelayCommand]
    private async Task ShareDiagnosticsAsync()
    {
        try
        {
            var path = await diagnosticLog.PrepareForSharingAsync(CancellationToken.None);
            if (path is null)
            {
                DiagnosticStatus = "There is nothing recorded to send.";
                return;
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Send the Forge diagnostic log",
                File = new ShareFile(path),
            });

            // Sharing is how a person acts on a crash notice, so the notice has done its job.
            // Leaving it up would keep reminding somebody of a fault they have already reported.
            diagnosticLog.AcknowledgeCrash();
            HasRecentCrash = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            DiagnosticStatus = ForgeUserFacingException.DescribeFor(ex, "Forge could not prepare the diagnostic log to send.");
        }
    }

    [RelayCommand]
    private async Task DeleteDiagnosticsAsync()
    {
        try
        {
            var reclaimed = await diagnosticLog.DeleteAsync(CancellationToken.None);
            HasRecentCrash = false;
            RecentCrashSummary = string.Empty;
            DiagnosticStatus = $"Deleted. {FormatSize(reclaimed)} freed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticStatus = ForgeUserFacingException.DescribeFor(ex, "Forge could not delete its diagnostic log.");
        }
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

    private static string FormatSize(long bytes)
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
