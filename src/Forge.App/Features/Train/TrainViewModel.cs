using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;

namespace Forge.App.Features.Train;

/// <summary>The training hub: the entry point into a session, the catalogue and past work.</summary>
public sealed partial class TrainViewModel : ObservableObject
{
    [RelayCommand]
    private static Task OpenExerciseLibraryAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.ExerciseLibrary);

    [RelayCommand]
    private static Task StartWorkoutAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.ActiveWorkout);

    [RelayCommand]
    private static Task OpenWorkoutHistoryAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.WorkoutHistory);

    [RelayCommand]
    private static Task OpenPlateCalculatorAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.PlateCalculator);

    [RelayCommand]
    private static Task OpenVideoLibraryAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.VideoLibrary);

    [RelayCommand]
    private static Task OpenMorningCheckInAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.MorningCheckIn);

    [RelayCommand]
    private static Task OpenReadinessAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.Readiness);

    [RelayCommand]
    private static Task OpenCoachingAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.Coaching);
}
