using Microsoft.AspNetCore.Http;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>The cookie's name and attributes (ADR-0005, FRONTEND-BFF §3).</summary>
public sealed class SessionCookieTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void The_cookie_is_Secure_except_in_Development_over_plain_http(bool isDevelopment, bool isHttps, bool expected)
    {
        Assert.Equal(expected, SessionCookie.RequiresSecure(isDevelopment, isHttps));
    }

    [Fact]
    public void A_secure_cookie_has_the_host_prefix_and_the_pinned_attributes()
    {
        var options = SessionCookie.Options(secure: true, expires: DateTimeOffset.UnixEpoch.AddDays(1));

        Assert.Equal("__Host-pc.session", SessionCookie.NameFor(secure: true));
        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.Strict, options.SameSite);
        Assert.Equal("/", options.Path);
    }

    [Fact]
    public void The_development_cookie_is_unprefixed_and_not_Secure()
    {
        var options = SessionCookie.Options(secure: false, expires: null);

        Assert.Equal("pc.session", SessionCookie.NameFor(secure: false));
        Assert.False(options.Secure);
        Assert.True(options.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, options.SameSite);
    }
}
