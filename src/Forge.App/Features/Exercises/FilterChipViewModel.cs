using CommunityToolkit.Mvvm.ComponentModel;

namespace Forge.App.Features.Exercises;

public sealed partial class FilterChipViewModel(string label, object? value) : ObservableObject
{
    public string Label { get; } = label;

    public object? Value { get; } = value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private bool isSelected;

    public string DisplayText => IsSelected ? $"✓ {Label}" : Label;
}
