using System.Globalization;

namespace Forge.App.Features.Workout;

/// <summary>Plate loading helper for a target weight.</summary>
public partial class PlateCalculatorPage : ContentPage, IQueryAttributable
{
    private readonly PlateCalculatorPageViewModel viewModel;
    private decimal? target;

    /// <summary>Creates the plate calculator page.</summary>
    /// <param name="viewModel">The page view model.</param>
    public PlateCalculatorPage(PlateCalculatorPageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc />
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TryGetValue("target", out var value)
            && decimal.TryParse(value?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            target = parsed;
        }
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        viewModel.Load(target);
    }
}
