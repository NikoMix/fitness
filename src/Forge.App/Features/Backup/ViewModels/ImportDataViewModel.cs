using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Backup;
using Forge.Domain.Profile;
using Microsoft.Maui.Storage;
using System.Globalization;

namespace Forge.App.Features.Backup.ViewModels;

public sealed partial class ImportDataViewModel(IDataImporter importer, ProfileStore profiles) : ObservableObject
{
    private string? selectedFile;

    [ObservableProperty]
    private string status = "Pick a Strong or Hevy CSV export. Forge previews detected rows before importing.";

    [ObservableProperty]
    private string previewSummary = "No file selected.";

    [ObservableProperty]
    private bool canImport;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private bool isBusy;

    [RelayCommand]
    private async Task PickFileAsync()
    {
        var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose Strong or Hevy CSV export" });
        if (picked is null)
        {
            return;
        }

        selectedFile = picked.FullPath;
        var preview = await importer.PreviewAsync(selectedFile, await SubjectAsync(), CancellationToken.None);
        CanImport = preview.CanImport;
        PreviewSummary = FormatPreview(preview);
        Status = preview.CanImport ? "Preview succeeded. Review the summary before importing." : "Preview found problems. Nothing has been imported.";
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (IsBusy || selectedFile is null)
        {
            return;
        }

        var subject = await SubjectAsync();
        var preview = await importer.PreviewAsync(selectedFile, subject, CancellationToken.None);
        if (!preview.CanImport)
        {
            CanImport = false;
            PreviewSummary = FormatPreview(preview);
            Status = "Import blocked by validation errors.";
            return;
        }

        var page = global::Microsoft.Maui.Controls.Shell.Current.CurrentPage;
        var confirmed = page is not null && await page.DisplayAlertAsync(
            "Import workout history?",
            DescribeConfirmation(preview),
            "Import",
            "Cancel");
        if (!confirmed)
        {
            Status = "Import cancelled.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await importer.ImportAsync(selectedFile, subject, new Progress<BackupProgress>(UpdateProgress), CancellationToken.None);
            Status = result.Message;
            PreviewSummary = FormatPreview(result.Preview);
            CanImport = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ProfileScope> SubjectAsync() => await profiles.GetActiveScopeAsync(CancellationToken.None);

    private static string DescribeConfirmation(ImportPreview preview)
    {
        var lines = new List<string>
        {
            preview.NewWorkoutCount == 1
                ? "Forge will add 1 workout to your log."
                : $"Forge will add {preview.NewWorkoutCount} workouts to your log.",
        };

        if (preview.AlreadyPresentWorkoutCount > 0)
        {
            // Stated before the write, not after it. Somebody importing a file they already
            // imported deserves to know nothing will change rather than discover it afterwards.
            lines.Add(preview.AlreadyPresentWorkoutCount == 1
                ? "1 workout in this file is already in your log and will be left exactly as it is."
                : $"{preview.AlreadyPresentWorkoutCount} workouts in this file are already in your log and will be left exactly as they are.");
        }

        lines.Add("If any row fails, no imported rows are kept.");
        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    private void UpdateProgress(BackupProgress update)
    {
        Status = update.Message;
        Progress = update.PercentComplete / 100d;
    }

    private static string FormatPreview(ImportPreview preview)
    {
        var range = preview.FromUtc is null ? "No dates" : $"{preview.FromUtc.Value.ToLocalTime():d} – {preview.ToUtc!.Value.ToLocalTime():d}";
        var errors = preview.Errors.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, preview.Errors.Take(5));
        var duplicates = preview.AlreadyPresentWorkoutCount == 0
            ? string.Empty
            : string.Create(CultureInfo.CurrentCulture, $"\nAlready in your log: {preview.AlreadyPresentWorkoutCount}");
        return string.Create(CultureInfo.CurrentCulture, $"Source: {preview.SourceApp}\nWorkouts: {preview.WorkoutCount}\nSets: {preview.SetCount}\nRange: {range}{duplicates}{errors}");
    }
}

