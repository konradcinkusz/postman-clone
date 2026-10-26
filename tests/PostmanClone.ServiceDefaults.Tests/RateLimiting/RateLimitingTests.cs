using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using PostmanClone.ServiceDefaults.RateLimiting;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.RateLimiting;

/// <summary>CONTRACTS §0: a limit answers a uniform 429, and a visitor is metered by who they are, not by a shared bucket.</summary>
public sealed class RateLimitingTests
{
    [Fact]
    public async Task The_auth_policy_allows_ten_a_minute_then_answers_one_uniform_429()
    {
        await using var app = await SecureTestHost.StartAsync(map: MapLimited);
        using var client = TestHost.ClientFor(app);

        for (var i = 0; i < 10; i++)
        {
            using var allowed = await client.GetAsync("/limited", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await client.GetAsync("/limited", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        Assert.Equal("rate_limited", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()));
    }

    [Fact]
    public void Two_signed_in_users_behind_one_address_are_metered_separately()
    {
        var first = ContextFor(subject: "user-a", remote: "10.0.0.1");
        var second = ContextFor(subject: "user-b", remote: "10.0.0.1");

        Assert.Equal("user:user-a", ClientIdentity.ForUser(first, trustForwardedFor: false));
        Assert.Equal("user:user-b", ClientIdentity.ForUser(second, trustForwardedFor: false));
    }

    [Fact]
    public void An_anonymous_visitor_is_metered_by_address()
    {
        var context = ContextFor(subject: null, remote: "10.0.0.1");

        Assert.Equal("ip:10.0.0.1", ClientIdentity.ForUser(context, trustForwardedFor: false));
    }

    [Fact]
    public void A_forwarded_address_is_ignored_unless_a_proxy_is_trusted()
    {
        var context = ContextFor(subject: null, remote: "10.0.0.1", forwardedFor: "203.0.113.9");

        Assert.Equal("ip:10.0.0.1", ClientIdentity.ForAddress(context, trustForwardedFor: false));
        Assert.Equal("ip:203.0.113.9", ClientIdentity.ForAddress(context, trustForwardedFor: true));
    }

    [Fact]
    public void The_last_forwarded_entry_is_the_one_the_proxy_appended()
    {
        // The left-most entry was sent by the client; the proxy appends the address it saw on the right.
        var context = ContextFor(subject: null, remote: "10.0.0.1", forwardedFor: "198.51.100.1, 203.0.113.9");

        Assert.Equal("ip:203.0.113.9", ClientIdentity.ForAddress(context, trustForwardedFor: true));
    }

    [Fact]
    public void An_unreadable_forwarded_entry_falls_back_to_the_socket_address()
    {
        var context = ContextFor(subject: null, remote: "10.0.0.1", forwardedFor: "not-an-address");

        Assert.Equal("ip:10.0.0.1", ClientIdentity.ForAddress(context, trustForwardedFor: true));
    }

    private static void MapLimited(WebApplication app) =>
        app.MapGet("/limited", () => Results.Ok())
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth);

    private static DefaultHttpContext ContextFor(string? subject, string remote, string? forwardedFor = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remote);
        if (forwardedFor is not null)
        {
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }

        if (subject is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", subject)],
                authenticationType: "Test"));
        }

        return context;
    }
}
