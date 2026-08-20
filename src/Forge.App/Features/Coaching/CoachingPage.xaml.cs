using Forge.App.Features.Coaching.ViewModels;

namespace Forge.App.Features.Coaching;

public partial class CoachingPage : ContentPage
{
    public CoachingPage(CoachingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
