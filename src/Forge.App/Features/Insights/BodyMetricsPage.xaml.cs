using Forge.App.Features.Insights.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Insights;

public partial class BodyMetricsPage : ContentPage
{
    private readonly BodyMetricsViewModel viewModel;

    public BodyMetricsPage()
        : this(ResolveViewModel())
    {
    }

    public BodyMetricsPage(BodyMetricsViewModel viewModel)
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

    private static BodyMetricsViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<BodyMetricsViewModel>()
        ?? throw new InvalidOperationException("The Body Metrics view model could not be resolved.");
}
