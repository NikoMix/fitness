using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Profile;

/// <summary>Profile summary, body metrics, goal and settings entry point.</summary>
public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel viewModel;

    /// <summary>Initialises the page for Shell tab activation.</summary>
    public ProfilePage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>Initialises the page.</summary>
    public ProfilePage(ProfileViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        this.viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync(CancellationToken.None);
    }

    private static ProfileViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<ProfileViewModel>()
        ?? throw new InvalidOperationException("The profile view model could not be resolved.");
}
