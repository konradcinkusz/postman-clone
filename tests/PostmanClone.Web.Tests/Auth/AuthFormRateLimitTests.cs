using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>A Web host whose auth window is three requests a minute, so the test reaches the limit in a few posts (OI-02).</summary>
public sealed class SmallWindowAuthHostFactory : AuthHostFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "3");
        base.ConfigureWebHost(builder);
    }
}

/// <summary>
/// The account forms share one fixed window per client address (OI-02). The limiter runs before antiforgery, so a post
/// without a token still counts, and the refusal is the kernel's 429 body with a Retry-After header.
/// </summary>
public sealed class AuthFormRateLimitTests : IClassFixture<SmallWindowAuthHostFactory>
{
    private readonly SmallWindowAuthHostFactory _factory;

    public AuthFormRateLimitTests(SmallWindowAuthHostFactory factory) => _factory = factory;

    [Fact]
    public async Task The_sign_in_and_register_forms_share_a_window_and_refuse_with_429_when_it_is_used()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        var cancellation = TestContext.Current.CancellationToken;

        // Three posts fill the window (they fail antiforgery, which is not the point here).
        for (var i = 0; i < 3; i++)
        {
            using var counted = await client.PostAsync("/sign-in", new FormUrlEncodedContent([]), cancellation);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, counted.StatusCode);
        }

        using var refusedSignIn = await client.PostAsync("/sign-in", new FormUrlEncodedContent([]), cancellation);
        Assert.Equal(HttpStatusCode.TooManyRequests, refusedSignIn.StatusCode);
        Assert.True(refusedSignIn.Headers.Contains("Retry-After"));
        using (var body = JsonDocument.Parse(await refusedSignIn.Content.ReadAsStringAsync(cancellation)))
        {
            Assert.Equal("rate_limited", body.RootElement.GetProperty("code").GetString());
        }

        // The register form is in the same window, so it is refused too.
        using var refusedRegister = await client.PostAsync("/register", new FormUrlEncodedContent([]), cancellation);
        Assert.Equal(HttpStatusCode.TooManyRequests, refusedRegister.StatusCode);
    }
}
