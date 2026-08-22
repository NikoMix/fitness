using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Backup;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace Forge.App.Features.Backup.ViewModels;

/// <summary>
/// The simple export screen, which always exports one profile's data.
/// </summary>
/// <remarks>
/// There is no device-wide switch here on purpose. This screen is reached from settings by
/// somebody wanting a copy of their own data, and a control that quietly widened that to everybody
/// on the device would be the easiest privacy mistake in the app to make by accident. The
/// deliberate, clearly-labelled whole-device option lives on the data portability screen.
/// </remarks>
public sealed partial class ExportDataViewModel(IDataExporter exporter, ProfileStore profiles) : ObservableObject
{
    [ObservableProperty]
    private bool exportJson = true;

    [ObservableProperty]
    private bool includeTraining = true;

    [ObservableProperty]
    private bool includeNutrition = true;

    [ObservableProperty]
    private bool includeProfile = true;

    [ObservableProperty]
    private bool limitDateRange;

    [ObservableProperty]
    private DateTime fromDate = DateTime.Today.AddMonths(-3);

    [ObservableProperty]
    private DateTime toDate = DateTime.Today;

    [ObservableProperty]
    private string status = "Exports the data Forge can attribute to you. Anything shared with other profiles on this device is listed but not included.";

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private bool isBusy;

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var selected = SelectedDataTypes();
        if (selected.Count == 0)
        {
            Status = "Choose at least one data type.";
            return;
        }

        IsBusy = true;
        try
        {
            var subject = await profiles.GetActiveScopeAsync(CancellationToken.None);
            var request = new ExportRequest(
                LimitDateRange ? new DateTimeOffset(FromDate.Date, TimeZoneInfo.Local.GetUtcOffset(FromDate.Date)).ToUniversalTime() : null,
                LimitDateRange ? new DateTimeOffset(ToDate.Date.AddDays(1).AddTicks(-1), TimeZoneInfo.Local.GetUtcOffset(ToDate.Date)).ToUniversalTime() : null,
                selected,
                ExportAudience.RequestingProfile,
                subject);

            var result = await exporter.ExportAsync(
                ExportJson ? ExportFormat.Json : ExportFormat.Csv,
                request,
                FileSystem.CacheDirectory,
                new Progress<BackupProgress>(UpdateProgress),
                CancellationToken.None);

            if (result.RecordCount == 0)
            {
                Status = subject.IsResolved
                    ? "Nothing was exported. Forge could not attribute any records to this profile."
                    : "No profile is active, so Forge could not tell whose data to export. Nothing was shared.";
                return;
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Share Forge export",
                File = new ShareFile(result.FilePath),
            });

            // The result describes itself, including what it had to leave out. Summarising it here
            // as a row count would restate a subset as if it were the whole record.
            Status = result.Describe();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private HashSet<ExportDataType> SelectedDataTypes()
    {
        var selected = new HashSet<ExportDataType>();
        if (IncludeTraining)
        {
            selected.Add(ExportDataType.Training);
        }

        if (IncludeNutrition)
        {
            selected.Add(ExportDataType.Nutrition);
        }

        if (IncludeProfile)
        {
            selected.Add(ExportDataType.Profile);
        }

        return selected;
    }

    private void UpdateProgress(BackupProgress update)
    {
        Status = update.Message;
        Progress = update.PercentComplete / 100d;
    }
}
