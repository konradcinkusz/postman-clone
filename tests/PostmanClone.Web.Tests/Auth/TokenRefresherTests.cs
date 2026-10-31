using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>Refresh under concurrency, under failure and after an organization is created (WP12 acceptance).</summary>
public sealed class TokenRefresherTests : IDisposable
{
    private readonly AuthHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Twenty_concurrent_callers_cause_exactly_one_refresh_and_all_receive_the_new_token()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);
        _harness.Clock.Advance(TimeSpan.FromMinutes(61)); // the access token is now inside its two-minute window.

        var callers = Enumerable.Range(0, 20)
            .Select(_ => _harness.Refresher.AccessTokenAsync(session.Id, touch: true, TestContext.Current.CancellationToken))
            .ToArray();
        var tokens = await Task.WhenAll(callers);

        Assert.Equal(1, _harness.Auth.RefreshCalls);
        Assert.All(tokens, token => Assert.Equal(_harness.Auth.LastAccessToken, token));
    }

    [Fact]
    public async Task A_token_that_is_not_near_expiry_is_used_without_a_refresh()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);

        var token = await _harness.Refresher.AccessTokenAsync(session.Id, touch: true, TestContext.Current.CancellationToken);

        Assert.Equal(session.Tokens.AccessToken, token);
        Assert.Equal(0, _harness.Auth.RefreshCalls);
    }

    [Fact]
    public async Task An_expired_refresh_token_ends_the_session()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);
        // Spend the refresh token elsewhere first: authservice rotates it, so the session's copy is no longer valid.
        await _harness.Client.RefreshAsync(session.Tokens.RefreshToken, TestContext.Current.CancellationToken);
        _harness.Clock.Advance(TimeSpan.FromMinutes(61));

        var token = await _harness.Refresher.AccessTokenAsync(session.Id, touch: true, TestContext.Current.CancellationToken);

        Assert.Null(token);
        Assert.Null(_harness.Store.Find(session.Id, touch: false));
    }

    [Fact]
    public async Task A_refresh_that_authservice_cannot_reach_also_ends_the_session()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);
        _harness.Clock.Advance(TimeSpan.FromMinutes(61));
        _harness.Auth.Unavailable = true;

        var token = await _harness.Refresher.AccessTokenAsync(session.Id, touch: true, TestContext.Current.CancellationToken);

        Assert.Null(token);
        Assert.Null(_harness.Store.Find(session.Id, touch: false));
    }

    [Fact]
    public async Task A_refresh_after_an_organization_is_created_puts_its_claim_on_the_session()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);
        Assert.False(WorkspaceClaims.Resolve(session.Principal).IsActive);

        await _harness.Client.CreateOrganizationAsync(session.Tokens.AccessToken, "Acme", TestContext.Current.CancellationToken);
        var refreshed = await _harness.Refresher.RefreshNowAsync(session.Id, TestContext.Current.CancellationToken);

        Assert.True(refreshed);
        var resolution = WorkspaceClaims.Resolve(session.Principal);
        Assert.True(resolution.IsActive);
        Assert.Equal(WorkspaceRoles.Owner, resolution.Role);
    }

    [Fact]
    public async Task The_principal_names_the_session_and_carries_no_token()
    {
        var session = await _harness.OpenSessionAsync(TestContext.Current.CancellationToken);

        var values = session.Principal.Claims.Select(claim => claim.Value).ToArray();

        Assert.Contains(session.Id, values);
        Assert.DoesNotContain(session.Tokens.AccessToken, values);
        Assert.DoesNotContain(session.Tokens.RefreshToken, values);
    }

    [Fact]
    public void The_token_pair_never_prints_its_tokens()
    {
        var tokens = new SessionTokens("access-value", "refresh-value", DateTimeOffset.UtcNow);

        Assert.Equal("SessionTokens(redacted)", tokens.ToString());
    }
}
