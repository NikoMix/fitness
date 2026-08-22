using Forge.App.Features.Engagement.ViewModels;

namespace Forge.App.Features.Engagement;

/// <summary>The rhythm screen.</summary>
public partial class StreaksPage : ContentPage
{
    private readonly StreaksPageViewModel viewModel;

    /// <summary>Creates the page.</summary>
    /// <param name="viewModel">The view model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public StreaksPage(StreaksPageViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Loaded on appearing rather than in the constructor. The page is resolved before it is
        // navigated to, so reading at construction would block navigation and would show numbers
        // from whichever profile was active when the page was built.
        await viewModel.LoadAsync(CancellationToken.None);
    }
}
