using System.Globalization;

namespace Forge.Core.Abstractions.Diagnostics;

/// <summary>
/// What the app leaves behind when it is about to die, so the next launch knows it did.
/// </summary>
/// <param name="OccurredAt">When the fault was caught.</param>
/// <param name="Source">Which hook caught it, for example <c>AppDomain</c> or <c>Android</c>.</param>
/// <param name="ExceptionType">The exception's type name. Never its message.</param>
public readonly record struct CrashBreadcrumb(DateTimeOffset OccurredAt, string Source, string ExceptionType)
{
    /// <summary>Name of the file the breadcrumb is written to.</summary>
    public const string FileName = "last-crash.txt";

    /// <summary>
    /// Records that the process is going down.
    /// </summary>
    /// <param name="directory">Directory the diagnostic files live in.</param>
    /// <param name="breadcrumb">What to record.</param>
    /// <returns><see langword="true"/> when the breadcrumb reached the disk.</returns>
    /// <remarks>
    /// <para>
    /// Three fields, written synchronously, into a file measured in tens of bytes. It carries the
    /// exception <em>type</em> and never its message, so a crash breadcrumb cannot become the one
    /// unredacted copy of an exception's text.
    /// </para>
    /// <para>
    /// This is what makes the crash boundary do something better than let the app vanish. An
    /// unhandled exception on the UI thread kills the process whatever Forge does about it -
    /// pretending otherwise would leave the app running on state nobody can reason about. What
    /// can be salvaged is the next launch: Forge knows it went down, can say so plainly, and can
    /// offer the log rather than leaving the user to describe a blank screen from memory.
    /// </para>
    /// </remarks>
    public static bool Write(string directory, CrashBreadcrumb breadcrumb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        try
        {
            Directory.CreateDirectory(directory);
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{breadcrumb.OccurredAt:O}\t{Sanitise(breadcrumb.Source)}\t{Sanitise(breadcrumb.ExceptionType)}");
            File.WriteAllText(Path.Combine(directory, FileName), line);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads the breadcrumb left by a previous launch, if there is one.</summary>
    /// <param name="directory">Directory the diagnostic files live in.</param>
    /// <returns>The breadcrumb, or <see langword="null"/> when the last launch ended normally.</returns>
    public static CrashBreadcrumb? Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        try
        {
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var parts = File.ReadAllText(path).Split('\t');
            if (parts.Length != 3 ||
                !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var occurredAt))
            {
                return null;
            }

            return new CrashBreadcrumb(occurredAt, parts[1], parts[2]);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes the breadcrumb, so a single crash is only reported once.</summary>
    /// <param name="directory">Directory the diagnostic files live in.</param>
    public static void Clear(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        try
        {
            var path = Path.Combine(directory, FileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Tabs and newlines are the record separator; a type name containing either would make the
    // file unparseable on the next launch.
    private static string Sanitise(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.ReplaceLineEndings(" ").Replace('\t', ' ');
}
