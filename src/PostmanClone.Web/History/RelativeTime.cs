using System.Globalization;

namespace PostmanClone.Web.History;

/// <summary>
/// "3 minutes ago" for the history list (WP22). The caller passes both instants, so the page reads the clock once through
/// <see cref="TimeProvider"/> and the rule is tested with a fake clock relative to now (no calendar date in any test).
/// An instant in the future (a clock that moved) reads as "just now", never as a negative span.
/// </summary>
public static class RelativeTime
{
    public static string Describe(DateTimeOffset then, DateTimeOffset now)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return Ago((int)span.TotalMinutes, "minute");
        }

        if (span < TimeSpan.FromDays(1))
        {
            return Ago((int)span.TotalHours, "hour");
        }

        if (span < TimeSpan.FromDays(30))
        {
            return Ago((int)span.TotalDays, "day");
        }

        return "on " + then.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Ago(int count, string unit) => $"{count} {unit}{(count == 1 ? string.Empty : "s")} ago";
}
