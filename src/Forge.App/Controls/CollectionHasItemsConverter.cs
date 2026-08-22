using System.Collections;
using System.Globalization;

namespace Forge.App.Controls;

/// <summary>
/// Reports whether a bound collection has anything in it.
/// </summary>
/// <remarks>
/// <para>
/// Forge lists are given a height so they occupy a predictable amount of a scrolling page. That is
/// fine while they have rows and wrong the moment they do not: an empty list still reserves its
/// height and draws a container with no text, no description and nothing to announce. The on-device
/// smoke harness caught exactly that on the food log - a 975x420 box under the "Recent" heading
/// holding two views and no content - and a screen-reader user meets it as a stretch of nothing
/// between two headings.
/// </para>
/// <para>
/// Binding a section's visibility through this converter means an empty list takes the section's
/// heading with it, rather than leaving a title over a void. Forge's deliberate empty states, which
/// carry explanatory copy, are unaffected: they are separate controls with their own visibility.
/// </para>
/// <para>
/// Set <see cref="Invert"/> for the companion case - copy that should appear only while a list is
/// empty.
/// </para>
/// </remarks>
public sealed class CollectionHasItemsConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets a value indicating whether the result is negated, so the binding is true while
    /// the collection is empty.
    /// </summary>
    public bool Invert { get; set; }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasItems = value switch
        {
            null => false,
            int count => count > 0,
            ICollection collection => collection.Count > 0,
            IEnumerable sequence => sequence.GetEnumerator().MoveNext(),
            _ => false,
        };

        return Invert ? !hasItems : hasItems;
    }

    /// <inheritdoc />
    /// <remarks>Visibility is derived from data and is never pushed back onto it.</remarks>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("CollectionHasItemsConverter is one-way.");
}
