namespace Forge.App.Features.Train;

public partial class TrainPage : ContentPage
{
    public TrainPage()
        : this(new TrainViewModel())
    {
    }

    public TrainPage(TrainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
