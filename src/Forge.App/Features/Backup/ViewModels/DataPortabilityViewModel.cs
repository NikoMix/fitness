using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Backup;
using Forge.Domain.Profile;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace Forge.App.Features.Backup.ViewModels;

/// <summary>
/// The data portability screen: one profile's data by default, the whole device only on purpose.
/// </summary>
/// <remarks>
/// <para>
/// The two options are not symmetrical and the screen does not pretend they are. A personal export
/// is what somebody asking for their data is entitled to under Article 20. A whole-device export
/// hands them every other profile's weight, food and training as well, which on a shared tablet is
/// a disclosure of somebody else's special-category data by the feature meant to protect it. So it
/// is off by default, labelled with what it actually contains, and confirmed before it runs.
/// </para>
/// <para>
/// What a personal export cannot include is shown before the export as well as after it. The
/// preview comes from <see cref="ProfileDataAreas"/>, the same derivation the profile switcher and
/// the deletion dialog use, so the three screens cannot drift into telling different stories. The
/// authoritative list is the one the finished export reports, because only that one is computed
/// from the rows actually read.
/// </para>
/// </remarks>
public sealed partial class DataPortabilityViewModel(IDataExporter exporter, ProfileStore profiles) : ObservableObject
{
    [ObservableProperty]
    private bool includeEveryProfile;

    [ObservableProperty]
    private bool includeSpreadsheets = true;

    [ObservableProperty]
    private string includedSummary = string.Empty;

    [ObservableProperty]
    private string excludedSummary = string.Empty;

    [ObservableProperty]
    private string status = "Forge exports the data it can attribute to you. Nothing leaves this device unless you share the file.";

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private bool isBusy;

    /// <summary>Loads the plain-English preview of what a personal export would contain.</summary>
    /// <returns>A task that completes when the summaries are set.</returns>
    public Task LoadAsync()
    {
        var separated = ProfileDataAreas.Separated();
        var shared = ProfileDataAreas.Shared();

        IncludedSummary = separated.Count == 0
            ? "Nothing on this device is separated by profile yet, so a personal export can only contain your profile itself."
            : "Included: " + string.Join(", ", separated.Select(area => area.Name)) + ", and your profile.";

        ExcludedSummary = shared.Count == 0
            ? "Every kind of data carries an owner, so nothing has to be left out."
            : "Left out, because these are shared between everybody on this device and Forge cannot tell which records are yours: "
                + string.Join(", ", shared.Select(area => area.Name)) + ".";

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var subject = await profiles.GetActiveScopeAsync(CancellationToken.None);
        if (!IncludeEveryProfile && !subject.IsResolved)
        {
            Status = "No profile is active, so Forge cannot tell whose data to export. Set up a profile first.";
            return;
        }

        if (IncludeEveryProfile && !await ConfirmDeviceWideAsync())
        {
            Status = "Export cancelled. Nothing was written.";
            return;
        }

        IsBusy = true;
        try
        {
            var request = IncludeEveryProfile ? ExportRequest.All : ExportRequest.ForProfile(subject);
            var result = await exporter.ExportAsync(
                IncludeSpreadsheets ? ExportFormat.Portable : ExportFormat.Json,
                request,
                FileSystem.CacheDirectory,
                new Progress<BackupProgress>(UpdateProgress),
                CancellationToken.None);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = IncludeEveryProfile ? "Share this device's data" : "Share your Forge data",
                File = new ShareFile(result.FilePath),
            });

            Status = result.Describe();
            ExcludedSummary = result.Unattributable.Count == 0
                ? ExcludedSummary
                : "Left out of the file you just shared: " + string.Join(", ", result.Unattributable.Select(item => item.Name)) + ".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = $"Export failed and no file was shared: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<bool> ConfirmDeviceWideAsync()
    {
        var page = Microsoft.Maui.Controls.Shell.Current?.CurrentPage;
        return page is not null && await page.DisplayAlertAsync(
            "Export everybody's data?",
            "This file will contain every record on this device, including the weight, food and training of any other profile. Only do this if the file is for you and you would be comfortable holding their health data.",
            "Include everybody",
            "Cancel");
    }

    private void UpdateProgress(BackupProgress update)
    {
        Status = update.Message;
        Progress = update.PercentComplete / 100d;
    }
}
