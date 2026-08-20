using Forge.App.Features.Engagement.ViewModels;

namespace Forge.App.Features.Engagement;

public partial class AchievementsPage : ContentPage
{
    public AchievementsPage(AchievementsPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.ShareRequested += OnShareRequested;
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
