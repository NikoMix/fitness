using CommunityToolkit.Mvvm.ComponentModel;
using Forge.Core.Abstractions.Preferences;

namespace Forge.App.Features.Settings.ViewModels;

public sealed class UnitsSettingsPageViewModel(IUnitPreferences preferences, IUnitFormatter formatter) : ObservableObject
{
    public IReadOnlyList<string> MassUnitOptions { get; } = ["Kilograms (kg)", "Pounds (lb)"];

    public IReadOnlyList<string> LengthUnitOptions { get; } = ["Centimetres (cm)", "Feet and inches"];

    public IReadOnlyList<string> VolumeUnitOptions { get; } = ["Millilitres (ml)", "Fluid ounces (fl oz)"];

    public IReadOnlyList<string> EnergyUnitOptions { get; } = ["Kilocalories (kcal)", "Kilojoules (kJ)"];

    public IReadOnlyList<string> FirstDayOptions { get; } =
        ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public string SelectedMassUnit
    {
        get => preferences.MassUnit == MassUnitPreference.Pounds ? MassUnitOptions[1] : MassUnitOptions[0];
        set
        {
            preferences.MassUnit = value == MassUnitOptions[1] ? MassUnitPreference.Pounds : MassUnitPreference.Kilograms;
            Refresh();
        }
    }

    public string SelectedLengthUnit
    {
        get => preferences.LengthUnit == LengthUnitPreference.FeetInches ? LengthUnitOptions[1] : LengthUnitOptions[0];
        set
        {
            preferences.LengthUnit = value == LengthUnitOptions[1] ? LengthUnitPreference.FeetInches : LengthUnitPreference.Centimeters;
            Refresh();
        }
    }

    public string SelectedVolumeUnit
    {
        get => preferences.VolumeUnit == VolumeUnitPreference.FluidOunces ? VolumeUnitOptions[1] : VolumeUnitOptions[0];
        set
        {
            preferences.VolumeUnit = value == VolumeUnitOptions[1] ? VolumeUnitPreference.FluidOunces : VolumeUnitPreference.Milliliters;
            Refresh();
        }
    }

    public string SelectedEnergyUnit
    {
        get => preferences.EnergyUnit == EnergyUnitPreference.Kilojoules ? EnergyUnitOptions[1] : EnergyUnitOptions[0];
        set
        {
            preferences.EnergyUnit = value == EnergyUnitOptions[1] ? EnergyUnitPreference.Kilojoules : EnergyUnitPreference.Kilocalories;
            Refresh();
        }
    }

    public string SelectedFirstDay
    {
        get => preferences.FirstDayOfWeek.ToString();
        set
        {
            preferences.FirstDayOfWeek = Enum.TryParse<DayOfWeek>(value, out var day) ? day : DayOfWeek.Monday;
            Refresh();
        }
    }

    public string PreviewMass => formatter.FormatMass(82.5);

    public string PreviewLength => formatter.FormatLength(180);

    public string PreviewVolume => formatter.FormatVolume(750);

    public string PreviewEnergy => formatter.FormatEnergy(2200);

    public string PreviewWeek => formatter.FormatFirstDayOfWeek();

    private void Refresh()
    {
        OnPropertyChanged(nameof(SelectedMassUnit));
        OnPropertyChanged(nameof(SelectedLengthUnit));
        OnPropertyChanged(nameof(SelectedVolumeUnit));
        OnPropertyChanged(nameof(SelectedEnergyUnit));
        OnPropertyChanged(nameof(SelectedFirstDay));
        OnPropertyChanged(nameof(PreviewMass));
        OnPropertyChanged(nameof(PreviewLength));
        OnPropertyChanged(nameof(PreviewVolume));
        OnPropertyChanged(nameof(PreviewEnergy));
        OnPropertyChanged(nameof(PreviewWeek));
    }
}
