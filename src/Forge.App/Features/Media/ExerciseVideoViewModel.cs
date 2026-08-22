using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Core.Abstractions.Media;
using Forge.Infrastructure.Content;

namespace Forge.App.Features.Media;

/// <summary>
/// Backs the exercise demonstration page.
/// </summary>
/// <remarks>
/// The source handed to the player is a <see cref="MediaSource"/> rather than a string on purpose.
/// The string form goes through a converter that reads anything parsing as an absolute URI as a
/// network address, so the <c>embed://</c> and <c>filesystem://</c> prefixes this used to build
/// were treated as remote URLs with invented schemes and never played, even when a file was sitting
/// on the device. Naming the file source outright removes the guess.
/// </remarks>
public sealed partial class ExerciseVideoViewModel(IMediaCatalogue mediaCatalogue, IMediaPlaybackPolicy playbackPolicy) : ObservableObject
{
    [ObservableProperty]
    private string title = "Exercise video";

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private MediaSource? playbackSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVideoLibrarySuggested))]
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

    /// <summary>
    /// Whether to offer the optional video packs, because nothing is playable here.
    /// </summary>
    /// <remarks>
    /// Only offered when there is no demonstration to watch. Video is an extra in Forge, and a
    /// standing invitation to download hundreds of megabytes on a page that is already complete
    /// would turn the extra into a nag.
    /// </remarks>
    public bool IsVideoLibrarySuggested => !HasMedia;

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
        PlaybackSource = ToPlayerSource(media);

        // The resolver knows which of several very different situations produced "no video" -
        // nothing downloaded, a pack that omits this movement, or a store lookup that failed - so
        // its sentence is shown rather than one guessed from the availability enum.
        AvailabilityMessage = media.TextDescription ?? media.Availability switch
        {
            ExerciseMediaAvailability.Bundled => "Bundled silent demonstration. Playback loops so you can compare each repetition.",
            ExerciseMediaAvailability.Downloaded => "Downloaded silent demonstration, played from this device. It never leaves it.",
            _ => "No demonstration video is available for this exercise. The text-only form guide below is the intended fallback."
        };

        CurrentDescription = SynchronizedDescriptions.FirstOrDefault() ?? "Use the written steps below to check your form.";
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

    [RelayCommand]
    private static Task OpenVideoLibraryAsync() => Shell.Current.GoToAsync(ForgeRoutes.VideoLibrary);

    /// <summary>
    /// Names the source for the player without letting a converter guess at it.
    /// </summary>
    /// <remarks>
    /// A downloaded asset pack hands back an absolute path on the device, and a bundled asset is a
    /// resource inside the app package. Both are local; neither is a URL.
    /// </remarks>
    private static MediaSource? ToPlayerSource(ExerciseMediaDescriptor media) => media.Availability switch
    {
        ExerciseMediaAvailability.Bundled when !string.IsNullOrWhiteSpace(media.Source) => MediaSource.FromResource(media.Source),
        ExerciseMediaAvailability.Downloaded when !string.IsNullOrWhiteSpace(media.Source) => MediaSource.FromFile(media.Source),
        _ => null
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

