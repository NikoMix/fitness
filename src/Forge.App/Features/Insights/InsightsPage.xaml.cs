using Forge.App.Features.Insights.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Insights;

public partial class InsightsPage : ContentPage
{
    private readonly InsightsViewModel viewModel;

    public InsightsPage()
        : this(ResolveViewModel())
    {
    }

    public InsightsPage(InsightsViewModel viewModel)
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

    private static InsightsViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<InsightsViewModel>()
        ?? throw new InvalidOperationException("The Insights view model could not be resolved.");
}
