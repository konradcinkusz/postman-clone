using PostmanClone.ExecutionService.Options;
using PostmanClone.ExecutionService.Sending.Ssrf;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Ssrf;

/// <summary>Each hop is re-validated, the method rules hold per status, and a cross-origin hop is flagged.</summary>
public sealed class RedirectPlannerTests
{
    private const string Start = "http://example.com/start";

    [Theory]
    [InlineData(200, "/next")]
    [InlineData(204, "/next")]
    [InlineData(300, "/next")]
    [InlineData(304, "/next")]
    [InlineData(301, null)]
    [InlineData(302, " ")]
    [InlineData(307, null)]
    [InlineData(303, null)]
    public void A_response_that_is_not_a_redirect_with_a_location_is_final(int status, string? location)
    {
        var step = Plan(status, "GET", location, redirectsFollowed: 0);

        Assert.Equal(RedirectOutcome.NotRedirect, step.Outcome);
    }

    [Theory]
    //  status  method  location                              -> method  dropBody  crossOrigin
    [InlineData(301, "GET", "/next", "GET", false, false)]
    [InlineData(302, "GET", "/next", "GET", false, false)]
    [InlineData(303, "GET", "/next", "GET", false, false)]
    [InlineData(303, "POST", "/next", "GET", true, false)]
    [InlineData(303, "PUT", "/next", "GET", true, false)]
    [InlineData(303, "HEAD", "/next", "HEAD", false, false)]
    [InlineData(301, "POST", "/next", "GET", true, false)]
    [InlineData(302, "POST", "/next", "GET", true, false)]
    [InlineData(302, "PUT", "/next", "PUT", false, false)]
    [InlineData(307, "POST", "/next", "POST", false, false)]
    [InlineData(308, "PUT", "/next", "PUT", false, false)]
    [InlineData(301, "GET", "http://example.com/next", "GET", false, false)]
    [InlineData(301, "GET", "HTTP://EXAMPLE.COM/next", "GET", false, false)]
    [InlineData(301, "GET", "//example.com/next", "GET", false, false)]
    [InlineData(307, "POST", "http://other.example/next", "POST", false, true)]
    [InlineData(301, "GET", "http://sub.example.com/", "GET", false, true)]
    [InlineData(301, "GET", "http://example.com:8080/next", "GET", false, true)]
    [InlineData(308, "GET", "https://example.com/next", "GET", false, true)]
    public void A_permitted_hop_keeps_or_changes_the_method_as_the_status_requires(
        int status, string method, string location, string expectedMethod, bool dropBody, bool crossOrigin)
    {
        var step = Plan(status, method, location, redirectsFollowed: 0);

        Assert.Equal(RedirectOutcome.Follow, step.Outcome);
        Assert.Equal(expectedMethod, step.Method);
        Assert.Equal(dropBody, step.DropBody);
        Assert.Equal(crossOrigin, step.CrossOrigin);
    }

    [Theory]
    [InlineData("http://127.0.0.1/", "ssrf_blocked_address")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "ssrf_blocked_address")]
    [InlineData("http://0x7f.1/", "ssrf_blocked_address")]
    [InlineData("http://0251.0376.0251.0376/", "ssrf_blocked_address")]
    [InlineData("http://[::ffff:127.0.0.1]/", "ssrf_blocked_address")]
    [InlineData("http://10.0.0.1/", "ssrf_blocked_address")]
    [InlineData("http://localhost/", "ssrf_blocked_address")]
    [InlineData("file:///etc/passwd", "ssrf_blocked_scheme")]
    [InlineData("gopher://example.com/", "ssrf_blocked_scheme")]
    [InlineData("http://example.com:22/", "ssrf_blocked_port")]
    [InlineData("http://example.com:5432/", "ssrf_blocked_port")]
    [InlineData("http://[::1", "invalid_url")]
    [InlineData("http://exa mple.com/", "invalid_url")]
    public void A_hop_to_a_refused_target_is_refused_with_its_code(string location, string errorCode)
    {
        var step = Plan(302, "GET", location, redirectsFollowed: 0);

        Assert.Equal(RedirectOutcome.Refuse, step.Outcome);
        Assert.Equal(errorCode, step.ErrorCode);
    }

    [Fact]
    public void A_relative_location_is_resolved_against_the_current_target()
    {
        var current = Target("http://example.com/a/b");

        var step = new RedirectPlanner(new ExecutionOptions()).Plan(current, "GET", 301, "../up?q=1", 0);

        Assert.Equal("http://example.com/up?q=1", step.Next!.Uri);
    }

    [Fact]
    public void A_redirect_from_https_to_http_is_refused_as_a_scheme_downgrade()
    {
        var current = Target("https://example.com/secure");

        var step = new RedirectPlanner(new ExecutionOptions()).Plan(current, "GET", 302, "http://example.com/plain", 0);

        Assert.Equal(RedirectOutcome.Refuse, step.Outcome);
        Assert.Equal("ssrf_blocked_scheme", step.ErrorCode);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void The_chain_may_hold_at_most_the_configured_number_of_redirects(int redirectsFollowed, bool followed)
    {
        var step = Plan(302, "GET", "/next", redirectsFollowed);

        Assert.Equal(followed, step.Outcome == RedirectOutcome.Follow);
        if (!followed)
        {
            Assert.Equal("too_many_redirects", step.ErrorCode);
        }
    }

    [Fact]
    public void With_no_redirects_allowed_the_first_redirect_is_refused()
    {
        var step = new RedirectPlanner(new ExecutionOptions { MaxRedirects = 0 })
            .Plan(Target(Start), "GET", 302, "/next", redirectsFollowed: 0);

        Assert.Equal("too_many_redirects", step.ErrorCode);
    }

    [Theory]
    [InlineData("Authorization", true)]
    [InlineData("authorization", true)]
    [InlineData("Proxy-Authorization", true)]
    [InlineData("Cookie", true)]
    [InlineData("X-Api-Key", true)]
    [InlineData("X-Auth-Token", true)]
    [InlineData("Accept", false)]
    [InlineData("Content-Type", false)]
    public void On_a_cross_origin_hop_the_credential_headers_are_dropped(string header, bool dropped)
    {
        var step = Plan(301, "GET", "http://other.example/next", redirectsFollowed: 0);

        Assert.Equal(dropped, RedirectPlanner.ShouldDropHeader(header, step));
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    public void On_a_same_origin_hop_the_credential_headers_are_kept(string header)
    {
        var step = Plan(301, "GET", "/next", redirectsFollowed: 0);

        Assert.False(RedirectPlanner.ShouldDropHeader(header, step));
    }

    [Fact]
    public void A_redirect_response_without_a_location_is_not_followed()
    {
        var step = Plan(302, "GET", null, redirectsFollowed: 0);

        Assert.Equal(RedirectOutcome.NotRedirect, step.Outcome);
    }

    private static RedirectStep Plan(int status, string method, string? location, int redirectsFollowed) =>
        new RedirectPlanner(new ExecutionOptions()).Plan(Target(Start), method, status, location, redirectsFollowed);

    private static SsrfTarget Target(string url) =>
        new TargetValidator(new ExecutionOptions()).Validate(url).Target!;
}
