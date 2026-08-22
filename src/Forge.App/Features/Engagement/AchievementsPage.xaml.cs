using Forge.App.Features.Engagement.ViewModels;

namespace Forge.App.Features.Engagement;

/// <summary>The achievements screen.</summary>
public partial class AchievementsPage : ContentPage
{
    private readonly AchievementsPageViewModel viewModel;

    /// <summary>Creates the page.</summary>
    /// <param name="viewModel">The view model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public AchievementsPage(AchievementsPageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.ShareRequested += OnShareRequested;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync(CancellationToken.None);
    }

    private async void OnShareRequested(object? sender, AchievementCardViewModel achievement)
    {
        ShareCard.BindingContext = achievement;
        ShareCard.IsVisible = true;

        var screenshot = await ShareCard.CaptureAsync();
        if (screenshot is null)
        {
            ShareCard.IsVisible = false;
            return;
        }

        var fileName = Path.Combine(FileSystem.CacheDirectory, $"forge-achievement-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.png");
        await using var stream = await screenshot.OpenReadAsync();
        await using var file = File.Create(fileName);
        await stream.CopyToAsync(file);
        ShareCard.IsVisible = false;

        await Share.RequestAsync(new ShareFileRequest
        {
            Title = achievement.Title,
            File = new ShareFile(fileName)
        });
    }
}
