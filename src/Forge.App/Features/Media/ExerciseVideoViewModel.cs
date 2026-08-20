using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions.Media;
using Forge.Infrastructure.Content;

namespace Forge.App.Features.Media;

public sealed partial class ExerciseVideoViewModel(IMediaCatalogue mediaCatalogue, IMediaPlaybackPolicy playbackPolicy) : ObservableObject
{
    [ObservableProperty]
    private string title = "Exercise video";

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private string mediaSource = string.Empty;

    [ObservableProperty]
    private bool hasMedia;

    [ObservableProperty]
    private bool shouldAutoPlay;

    [ObservableProperty]
    private string availabilityMessage = "Loading exercise guidance.";

    [ObservableProperty]
    private string currentDescription = "Use the written steps below to check your form.";

    [ObservableProperty]
    private double speed = 1.0;

    [ObservableProperty]
    private string currentPositionText = "0:00";

    [ObservableProperty]
    private string durationText = "0:00";

    [ObservableProperty]
    private bool isFullScreen;

    [ObservableProperty]
    private string fullScreenButtonText = "Full screen";

    public ObservableCollection<string> ExecutionSteps { get; } = [];

    public ObservableCollection<string> CoachingCues { get; } = [];

    public ObservableCollection<string> CommonMistakes { get; } = [];

    public ObservableCollection<string> SynchronizedDescriptions { get; } = [];

    public async Task LoadAsync(string exerciseName, CancellationToken cancellationToken = default)
    {
        var exercise = SeedCatalogue.FindByName(exerciseName) ?? SeedCatalogue.Exercises[0];
        Title = exercise.Name;
        Summary = $"{exercise.Pattern} • {(string.IsNullOrWhiteSpace(exercise.Equipment) ? "Bodyweight" : exercise.Equipment)} • {exercise.Difficulty}";

        Replace(ExecutionSteps, exercise.ExecutionSteps);
        Replace(CoachingCues, exercise.CoachingCues);
        Replace(CommonMistakes, exercise.CommonMistakes);
        Replace(SynchronizedDescriptions, exercise.ExecutionSteps.Select((step, index) => $"Step {index + 1}: {step}"));

        var media = await mediaCatalogue.ResolveExerciseMediaAsync(exercise.Name, cancellationToken);
        HasMedia = media.HasPlayableSource;
        MediaSource = ToMauiSource(media);
        AvailabilityMessage = media.Availability switch
        {
            ExerciseMediaAvailability.Bundled => "Bundled silent demonstration. Playback loops so you can compare each repetition.",
            ExerciseMediaAvailability.Downloaded => "Downloaded silent demonstration stored in the device cache. It never leaves this device.",
            _ => "No motion asset is installed for this exercise in v1. The text-only form guide below is the intended fallback."
        };

        CurrentDescription = media.TextDescription ?? SynchronizedDescriptions.FirstOrDefault() ?? "Use the written steps below to check your form.";
        ShouldAutoPlay = HasMedia && !playbackPolicy.ShouldSuppressAutoplay();
    }

    public void UpdatePlaybackClock(TimeSpan position, TimeSpan duration)
    {
        CurrentPositionText = Format(position);
        DurationText = Format(duration);

        if (SynchronizedDescriptions.Count == 0 || duration <= TimeSpan.Zero)
        {
            return;
        }

        var ratio = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 0.999);
        var index = Math.Min(SynchronizedDescriptions.Count - 1, (int)(ratio * SynchronizedDescriptions.Count));
        CurrentDescription = SynchronizedDescriptions[index];
    }

    [RelayCommand]
    private void SetSpeed(string requestedSpeed)
    {
        if (double.TryParse(requestedSpeed, System.Globalization.CultureInfo.InvariantCulture, out var parsedSpeed))
        {
            Speed = Math.Clamp(parsedSpeed, 0.25, 2.0);
        }
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        IsFullScreen = !IsFullScreen;
        FullScreenButtonText = IsFullScreen ? "Exit full screen" : "Full screen";
    }

    private static string ToMauiSource(ExerciseMediaDescriptor media) => media.Availability switch
    {
        ExerciseMediaAvailability.Bundled when !string.IsNullOrWhiteSpace(media.Source) => $"embed://{media.Source}",
        ExerciseMediaAvailability.Downloaded when !string.IsNullOrWhiteSpace(media.Source) => $"filesystem://{media.Source}",
        _ => string.Empty
    };

    private static string Format(TimeSpan value) => $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            target.Add(value);
        }
    }
}

