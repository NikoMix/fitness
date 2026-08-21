using System.Globalization;

namespace Forge.Core.Abstractions.Localization;

/// <summary>Thrown when a string key resolves in no culture and the strict policy is in force.</summary>
/// <seealso cref="MissingLocalizedStringBehavior.Throw"/>
public sealed class MissingLocalizedStringException : Exception
{
    /// <summary>Creates the exception.</summary>
    public MissingLocalizedStringException()
        : base("A localized string could not be resolved.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public MissingLocalizedStringException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public MissingLocalizedStringException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a specific unresolved key.</summary>
    /// <param name="key">The unresolved resource key.</param>
    /// <param name="culture">The culture the lookup started from.</param>
    public MissingLocalizedStringException(string key, CultureInfo culture)
        : base(FormattableString.Invariant($"No translation for '{key}' in '{culture?.Name}' or any fallback culture."))
    {
        Key = key;
        CultureName = culture?.Name;
    }

    /// <summary>The unresolved resource key, when known.</summary>
    public string? Key { get; }

    /// <summary>The culture the failed lookup started from, when known.</summary>
    public string? CultureName { get; }
}
