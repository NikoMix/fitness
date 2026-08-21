namespace Forge.Core.Abstractions.Health;

/// <summary>
/// Formats health sync timestamps for display.
/// </summary>
/// <remarks>
/// Lives in <c>Forge.Core</c> so the wording is unit-tested against a fixed clock rather than
/// eyeballed on a device. "Last synced" is one of the few places where a subtly wrong label - an
/// hour-old sync shown as "just now" - actively misleads someone deciding whether to trust a number.
/// </remarks>
public static class HealthSyncLabels
{
    /// <summary>Describes when a category last synced.</summary>
    /// <param name="lastSyncedUtc">The last successful read, or null if it never synced.</param>
    /// <param name="nowUtc">Current time.</param>
    /// <returns>A short phrase such as "3 hours ago" or "Never synced".</returns>
    public static string DescribeLastSync(DateTimeOffset? lastSyncedUtc, DateTimeOffset nowUtc)
    {
        if (lastSyncedUtc is not { } lastSynced)
        {
            return "Never synced";
        }

        // Clock changes, time-zone travel and a restored backup can all put a stored timestamp in
        // the future. Showing "in 3 hours" would look like a defect, so treat it as current.
        var elapsed = nowUtc - lastSynced;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "Synced just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;
            return $"Synced {minutes} {Plural(minutes, "minute")} ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;
            return $"Synced {hours} {Plural(hours, "hour")} ago";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            var days = (int)elapsed.TotalDays;
            return $"Synced {days} {Plural(days, "day")} ago";
        }

        return $"Synced on {lastSynced.ToLocalTime():d MMMM yyyy}";
    }

    private static string Plural(int count, string singular) => count is 1 ? singular : singular + "s";
}
