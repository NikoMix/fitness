using Forge.App.Features.Insights.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Insights;

public partial class ExerciseProgressPage : ContentPage
{
    private readonly ExerciseProgressViewModel viewModel;

    public ExerciseProgressPage()
        : this(ResolveViewModel())
    {
    }

    public ExerciseProgressPage(ExerciseProgressViewModel viewModel)
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

    private static ExerciseProgressViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<ExerciseProgressViewModel>()
        ?? throw new InvalidOperationException("The Exercise Progress view model could not be resolved.");
}
