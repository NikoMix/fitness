using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Media;

public partial class ExerciseVideoPage : ContentPage, IQueryAttributable
{
    private static readonly TimeSpan FrameStep = TimeSpan.FromSeconds(1d / 30d);
    private readonly ExerciseVideoViewModel viewModel;
    private readonly double defaultVideoHeight;
    private bool isScrubbing;

    public ExerciseVideoPage()
        : this(ResolveViewModel())
    {
    }

    public ExerciseVideoPage(ExerciseVideoViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        defaultVideoHeight = Token("TouchTargetPrimary") * 4;
        DemoPlayer.HeightRequest = defaultVideoHeight;
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged += OnSizeChanged;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("forge.parameter", out var value) && value is string exerciseName)
        {
            await viewModel.LoadAsync(exerciseName);
        }
    }

    protected override void OnDisappearing()
    {
        DemoPlayer.Stop();
        base.OnDisappearing();
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        ScrubSlider.Maximum = Math.Max(0, DemoPlayer.Duration.TotalSeconds);
        viewModel.UpdatePlaybackClock(DemoPlayer.Position, DemoPlayer.Duration);
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        if (!isScrubbing)
        {
            ScrubSlider.Value = Math.Clamp(DemoPlayer.Position.TotalSeconds, ScrubSlider.Minimum, ScrubSlider.Maximum);
        }

        viewModel.UpdatePlaybackClock(DemoPlayer.Position, DemoPlayer.Duration);
    }

    private void OnMediaFailed(object? sender, EventArgs e)
    {
        viewModel.AvailabilityMessage = "This media asset could not be played. The text-only form guide remains available below.";
    }

    private void OnScrubDragStarted(object? sender, EventArgs e) => isScrubbing = true;

    private async void OnScrubDragCompleted(object? sender, EventArgs e)
    {
        isScrubbing = false;
        await SeekAsync(TimeSpan.FromSeconds(ScrubSlider.Value));
    }

    private void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        if (DemoPlayer.CurrentState is MediaElementState.Playing or MediaElementState.Buffering)
        {
            DemoPlayer.Pause();
            return;
        }

        DemoPlayer.Play();
    }

    private async void OnFrameBackClicked(object? sender, EventArgs e)
    {
        DemoPlayer.Pause();
        await SeekAsync(DemoPlayer.Position - FrameStep);
    }

    private async void OnFrameForwardClicked(object? sender, EventArgs e)
    {
        DemoPlayer.Pause();
        await SeekAsync(DemoPlayer.Position + FrameStep);
    }

    private async Task SeekAsync(TimeSpan requested)
    {
        var duration = DemoPlayer.Duration <= TimeSpan.Zero ? TimeSpan.MaxValue : DemoPlayer.Duration;
        var target = requested < TimeSpan.Zero ? TimeSpan.Zero : requested > duration ? duration : requested;
        await DemoPlayer.SeekTo(target, CancellationToken.None);
        viewModel.UpdatePlaybackClock(target, DemoPlayer.Duration);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExerciseVideoViewModel.IsFullScreen))
        {
            ApplyFullScreen(viewModel.IsFullScreen);
        }
    }

    private void ApplyFullScreen(bool isFullScreen)
    {
        Shell.SetNavBarIsVisible(this, !isFullScreen);
        Shell.SetTabBarIsVisible(this, !isFullScreen);
        UpdateVideoHeight();
    }

    private void OnSizeChanged(object? sender, EventArgs e) => UpdateVideoHeight();

    private void UpdateVideoHeight()
    {
        if (viewModel.IsFullScreen)
        {
            DemoPlayer.HeightRequest = Math.Max(defaultVideoHeight, Height);
            return;
        }

        DemoPlayer.HeightRequest = Width > Height && Height > 0
            ? Math.Max(defaultVideoHeight, Height * 0.55)
            : defaultVideoHeight;
    }

    private static double Token(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is double token)
        {
            return token;
        }

        return 64;
    }

    private static ExerciseVideoViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<ExerciseVideoViewModel>()
        ?? throw new InvalidOperationException("The exercise video view model could not be resolved.");
}
