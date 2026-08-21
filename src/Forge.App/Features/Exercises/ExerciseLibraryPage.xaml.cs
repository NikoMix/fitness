using System.ComponentModel;
using Forge.App.Adaptive;

namespace Forge.App.Features.Exercises;

public partial class ExerciseLibraryPage : ContentPage
{
    private readonly ExerciseLibraryViewModel viewModel;

    public ExerciseLibraryPage(ExerciseLibraryViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;

        // The view model decides whether View opens a page or fills the pane, so it has to be told
        // what the window is currently doing. This is pushed from the measured width rather than
        // read from the device idiom, because an iPad changes width while the app is running.
        Adaptive.PropertyChanged += OnAdaptiveLayoutChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.IsSplitLayout = Adaptive.IsSplit;
        _ = viewModel.LoadAsync();
    }

    private void OnAdaptiveLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdaptiveHost.IsSplit))
        {
            viewModel.IsSplitLayout = Adaptive.IsSplit;
        }
    }
}
