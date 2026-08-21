using System.ComponentModel;
using Forge.App.Motion;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Onboarding;

/// <summary>Step-by-step first-run goal setup.</summary>
public partial class GoalWizardPage : ContentPage
{
    private readonly GoalWizardViewModel viewModel;

    /// <summary>Initialises the page for XAML route activation.</summary>
    public GoalWizardPage()
        : this(ResolveViewModel())
    {
    }

    /// <summary>Initialises the page.</summary>
    /// <param name="viewModel">The wizard view model.</param>
    public GoalWizardPage(GoalWizardViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await viewModel.InitialiseAsync().ConfigureAwait(true);
        }
        catch (InvalidOperationException exception)
        {
            // This override is async void, so nothing observes an escaping exception and the
            // process would simply die on the first screen of the app. An empty wizard is a far
            // better outcome than a crash: every answer can still be entered by hand.
            System.Diagnostics.Debug.WriteLine($"Goal wizard could not restore previous answers: {exception}");
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Leaving mid-setup - backgrounded, a phone call, a deliberate exit - must not cost the
        // answers already given. This is the resumable half of the draft: InitialiseAsync reads it
        // back and reopens on the first step that is still incomplete.
        viewModel.PersistDraft();
    }

    /// <summary>
    /// Routes the Android hardware back key to the wizard's own back step.
    /// </summary>
    /// <returns><see langword="true"/> when the key was handled in-page.</returns>
    /// <remarks>
    /// Without this the hardware key pops the entire wizard from step five, which is the single
    /// most expensive way to lose someone during first run.
    /// </remarks>
    protected override bool OnBackButtonPressed() => viewModel.TryGoBack() || base.OnBackButtonPressed();

    private static GoalWizardViewModel ResolveViewModel() =>
        Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<GoalWizardViewModel>()
        ?? throw new InvalidOperationException("The goal wizard view model could not be resolved.");

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GoalWizardViewModel.CurrentStep))
        {
            return;
        }

        // A short fade marks that the content changed underneath a header that did not move.
        // ForgeAnimations skips this entirely when Reduce Motion is on, in which case the step
        // simply appears - which is the correct behaviour, not a degraded one.
        _ = ForgeAnimations.FadeInAsync(StepHost, MotionTokens.Fast);
    }
}
