using Forge.App.Features.Coaching.ViewModels;

namespace Forge.App.Features.Coaching;

public partial class ReadinessPage : ContentPage
{
    public ReadinessPage(ReadinessViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
