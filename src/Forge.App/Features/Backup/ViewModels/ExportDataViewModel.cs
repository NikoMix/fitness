using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions.Backup;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using System.Globalization;

namespace Forge.App.Features.Backup.ViewModels;

public sealed partial class ExportDataViewModel(IDataExporter exporter) : ObservableObject
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
    private string status = "Export JSON for a complete archive or CSV for spreadsheets.";

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
            var request = new ExportRequest(
                LimitDateRange ? new DateTimeOffset(FromDate.Date, TimeZoneInfo.Local.GetUtcOffset(FromDate.Date)).ToUniversalTime() : null,
                LimitDateRange ? new DateTimeOffset(ToDate.Date.AddDays(1).AddTicks(-1), TimeZoneInfo.Local.GetUtcOffset(ToDate.Date)).ToUniversalTime() : null,
                selected);
            var result = await exporter.ExportAsync(ExportJson ? ExportFormat.Json : ExportFormat.Csv, request, FileSystem.CacheDirectory, new Progress<BackupProgress>(UpdateProgress), CancellationToken.None);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Share Forge export",
                File = new ShareFile(result.FilePath),
            });
            Status = $"Export ready: {result.RecordCounts.Sum(static pair => pair.Value)} rows shared.";
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
