using CommunityToolkit.Mvvm.ComponentModel;

namespace Forge.App.Features.Exercises;

/// <summary>A single toggleable filter chip.</summary>
/// <remarks>
/// Chips toggle independently rather than behaving like radio buttons. "Dumbbell or bodyweight"
/// is the question people ask standing in a gym, and forcing one choice per axis makes that
/// question unanswerable.
/// </remarks>
/// <param name="label">The text shown on the chip.</param>
/// <param name="value">The value this chip contributes to the filter when selected.</param>
public sealed partial class FilterChipViewModel(string label, object? value) : ObservableObject
{
    /// <summary>The text shown on the chip.</summary>
    public string Label { get; } = label;

    /// <summary>The value this chip contributes to the filter when selected.</summary>
    public object? Value { get; } = value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(AccessibilityDescription))]
    private bool isSelected;

    /// <summary>The chip text, marked when selected.</summary>
    public string DisplayText => IsSelected ? $"✓ {Label}" : Label;

    /// <summary>
    /// The chip's spoken description.
    /// </summary>
    /// <remarks>
    /// A tick prefix is invisible to a screen reader as a state change, so selection is spelled
    /// out in words. Without this a blind user cannot tell which filters are active.
    /// </remarks>
    public string AccessibilityDescription => IsSelected ? $"{Label} filter, selected" : $"{Label} filter, not selected";
}
