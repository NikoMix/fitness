using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Coaching.Services;
using Forge.Domain.Recovery;

namespace Forge.App.Features.Coaching.ViewModels;

public sealed partial class MorningCheckInViewModel(ICoachingDataService dataService) : ObservableObject
{
    [ObservableProperty]
    private double energy = 3;

    [ObservableProperty]
    private double soreness = 2;

    [ObservableProperty]
    private double motivation = 3;

    [ObservableProperty]
    private double stress = 3;

    [ObservableProperty]
    private double? sleepHours;

    [ObservableProperty]
    private string saveStatus = "Health sleep is optional; manual input is enough.";

    public IAsyncRelayCommand SaveCommand => new AsyncRelayCommand(SaveAsync);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var checkIn = new MorningCheckIn
        {
            Date = DateOnly.FromDateTime(DateTime.Now),
            Energy = ClampFivePoint(Energy),
            Soreness = ClampFivePoint(Soreness),
            Motivation = ClampFivePoint(Motivation),
            Stress = ClampFivePoint(Stress),
            SleepHours = SleepHours.HasValue ? decimal.Round((decimal)SleepHours.Value, 2) : null
        };

        await dataService.SaveMorningCheckInAsync(checkIn, cancellationToken).ConfigureAwait(false);
        SaveStatus = "Check-in saved locally.";
    }

    private static int ClampFivePoint(double value) => Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 1, 5);
}
