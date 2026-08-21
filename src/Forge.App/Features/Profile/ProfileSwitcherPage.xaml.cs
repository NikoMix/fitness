using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Profile;

/// <summary>Choosing which local profile is using this device.</summary>
public partial class ProfileSwitcherPage : ContentPage
{
    private readonly ProfileSwitcherViewModel viewModel;

    /// <summary>Initialises the page for Shell route activation.</summary>
    public ProfileSwitcherPage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>Initialises the page.</summary>
    /// <param name="viewModel">The switcher view model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public ProfileSwitcherPage(ProfileSwitcherViewModel viewModel)
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

    private static ProfileSwitcherViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<ProfileSwitcherViewModel>()
        ?? throw new InvalidOperationException("The profile switcher view model could not be resolved.");
}
