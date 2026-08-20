using Forge.App.Features.Today.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Today;

public partial class TodayPage : ContentPage
{
    private readonly TodayViewModel viewModel;

    public TodayPage()
        : this(ResolveViewModel())
    {
    }

    public TodayPage(TodayViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    private static TodayViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<TodayViewModel>()
        ?? throw new InvalidOperationException("The Today view model could not be resolved.");
}
