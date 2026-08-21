using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Media.Library;

/// <summary>
/// Page that lets the user manage optional exercise video packs.
/// </summary>
public partial class VideoLibraryPage : ContentPage
{
    private readonly VideoLibraryViewModel viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoLibraryPage"/> class.
    /// </summary>
    public VideoLibraryPage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoLibraryPage"/> class.
    /// </summary>
    /// <param name="viewModel">Page view model.</param>
    public VideoLibraryPage(VideoLibraryViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    private static VideoLibraryViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<VideoLibraryViewModel>()
        ?? throw new InvalidOperationException("The video library view model could not be resolved.");
}
