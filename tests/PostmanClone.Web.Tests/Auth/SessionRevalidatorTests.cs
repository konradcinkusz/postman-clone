using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>The circuit's five-minute check, against the store's clock (WP12: "circuit revalidation test with FakeTimeProvider").</summary>
public sealed class SessionRevalidatorTests : IDisposable
{
    private readonly AuthHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void The_revalidation_interval_is_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), PostmanClone.Web.Auth.SessionRevalidator.Interval);
    }

    [Fact]
    public async Task A_live_session_stays_valid()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);

        Assert.True(await _harness.Revalidator.IsStillValidAsync(session.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_idle_session_ends_after_eight_hours_without_a_request()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);

        _harness.Clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));

        Assert.False(await _harness.Revalidator.IsStillValidAsync(session.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_revalidation_is_not_activity_so_an_open_idle_tab_still_times_out()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);

        // Five-minute checks for the whole idle period, and no request: the session still ends at eight hours.
        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1); elapsed += SessionRevalidator.Interval)
        {
            _harness.Clock.Advance(SessionRevalidator.Interval);
            await _harness.Revalidator.IsStillValidAsync(session.Id, TestContext.Current.CancellationToken);
        }

        Assert.False(await _harness.Revalidator.IsStillValidAsync(session.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_request_every_six_hours_keeps_the_session_but_the_absolute_lifetime_still_ends_it()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);

        var elapsed = TimeSpan.Zero;
        while (elapsed + TimeSpan.FromHours(6) < ServerSessionStore.AbsoluteLifetime)
        {
            _harness.Clock.Advance(TimeSpan.FromHours(6));
            elapsed += TimeSpan.FromHours(6);
            Assert.NotNull(_harness.Store.Find(session.Id, touch: true));
        }

        _harness.Clock.Advance(ServerSessionStore.AbsoluteLifetime - elapsed);
        Assert.Null(_harness.Store.Find(session.Id, touch: false));
    }

    [Fact]
    public async Task A_refused_refresh_fails_revalidation()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);
        await _harness.Client.RefreshAsync(session.Tokens.RefreshToken, TestContext.Current.CancellationToken);
        _harness.Clock.Advance(TimeSpan.FromMinutes(61));

        Assert.False(await _harness.Revalidator.IsStillValidAsync(session.Id, TestContext.Current.CancellationToken));
    }
}
