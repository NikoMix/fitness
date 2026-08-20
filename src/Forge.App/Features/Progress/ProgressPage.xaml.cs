using Forge.App.Features.Progress.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Progress;

public partial class ProgressPage : ContentPage
{
    private readonly ProgressViewModel viewModel;

    public ProgressPage()
        : this(ResolveViewModel())
    {
    }

    public ProgressPage(ProgressViewModel viewModel)
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

    private static ProgressViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<ProgressViewModel>()
        ?? throw new InvalidOperationException("The Progress view model could not be resolved.");
}
