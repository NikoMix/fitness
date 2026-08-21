using Forge.App.Features.Health.ViewModels;

namespace Forge.App.Features.Health;

/// <summary>The health connections screen.</summary>
public partial class HealthConnectionsPage : ContentPage
{
    private readonly HealthConnectionsViewModel viewModel;

    /// <summary>Creates the page.</summary>
    /// <param name="viewModel">The screen's view model.</param>
    /// <exception cref="ArgumentNullException"><paramref name="viewModel"/> is null.</exception>
    public HealthConnectionsPage(HealthConnectionsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Load, never Connect. Prompting for health permissions the moment a screen appears asks
        // for special-category data before the user has expressed any interest, which both stores'
        // guidelines discourage and which users reliably refuse - and a refusal is not easily undone.
        if (viewModel.LoadCommand.CanExecute(null))
        {
            viewModel.LoadCommand.Execute(null);
        }
    }
}
