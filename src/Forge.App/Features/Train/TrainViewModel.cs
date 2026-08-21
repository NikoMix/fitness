using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;

namespace Forge.App.Features.Train;

public sealed partial class TrainViewModel : ObservableObject
{
    [RelayCommand]
    private static Task OpenExerciseLibraryAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.ExerciseLibrary);
}
