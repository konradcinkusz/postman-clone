using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.Web.History;

namespace PostmanClone.Web.Tests.History;

/// <summary>
/// The history's relative times, read against a fake clock and offsets from its now. No test names or pins a calendar date.
/// </summary>
public sealed class RelativeTimeTests
{
    private readonly FakeTimeProvider _clock = new();

    private DateTimeOffset Now => _clock.GetUtcNow();

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    public void A_moment_ago_is_just_now(int seconds)
    {
        Assert.Equal("just now", RelativeTime.Describe(Now.AddSeconds(-seconds), Now));
    }

    [Fact]
    public void An_instant_in_the_future_reads_as_just_now_not_a_negative_span()
    {
        Assert.Equal("just now", RelativeTime.Describe(Now.AddMinutes(5), Now));
    }

    [Theory]
    [InlineData(1, "1 minute ago")]
    [InlineData(2, "2 minutes ago")]
    [InlineData(59, "59 minutes ago")]
    public void Minutes_are_counted_and_pluralised(int minutes, string expected)
    {
        Assert.Equal(expected, RelativeTime.Describe(Now.AddMinutes(-minutes), Now));
    }

    [Theory]
    [InlineData(1, "1 hour ago")]
    [InlineData(23, "23 hours ago")]
    public void Hours_are_counted_and_pluralised(int hours, string expected)
    {
        Assert.Equal(expected, RelativeTime.Describe(Now.AddHours(-hours), Now));
    }

    [Theory]
    [InlineData(1, "1 day ago")]
    [InlineData(29, "29 days ago")]
    public void Days_are_counted_until_a_month(int days, string expected)
    {
        Assert.Equal(expected, RelativeTime.Describe(Now.AddDays(-days), Now));
    }

    [Fact]
    public void An_older_entry_reads_as_its_date_in_utc()
    {
        var then = Now.AddDays(-45);

        Assert.Equal("on " + then.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), RelativeTime.Describe(then, Now));
    }

    [Fact]
    public void The_clock_is_read_through_the_time_provider_so_a_fake_clock_moves_the_text()
    {
        var entry = Now;

        _clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal("3 minutes ago", RelativeTime.Describe(entry, Now));
    }
}
