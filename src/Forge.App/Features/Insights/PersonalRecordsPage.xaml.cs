using Forge.App.Features.Insights.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Insights;

public partial class PersonalRecordsPage : ContentPage
{
    private readonly PersonalRecordsViewModel viewModel;

    public PersonalRecordsPage()
        : this(ResolveViewModel())
    {
    }

    public PersonalRecordsPage(PersonalRecordsViewModel viewModel)
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

    private static PersonalRecordsViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<PersonalRecordsViewModel>()
        ?? throw new InvalidOperationException("The Personal Records view model could not be resolved.");
}
