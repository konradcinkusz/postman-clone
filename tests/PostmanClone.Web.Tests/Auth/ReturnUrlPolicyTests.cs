using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>The ReturnUrl rules: a local path only (WP12 acceptance, FRONTEND-BFF §4).</summary>
public sealed class ReturnUrlPolicyTests
{
    [Theory]
    [InlineData("//evil.example/steal")]
    [InlineData("///evil.example")]
    [InlineData("https://evil.example/steal")]
    [InlineData("http:evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("/\\\\evil.example")]
    [InlineData("evil.example/steal")]
    [InlineData("/workspace\r\nLocation: https://evil.example")]
    [InlineData("/workspace\tx")]
    [InlineData("/sign-in?ReturnUrl=/")]
    [InlineData("")]
    [InlineData(null)]
    public void A_return_url_that_is_not_a_local_path_falls_back_to_home(string? returnUrl)
    {
        Assert.Equal(ReturnUrlPolicy.Fallback, ReturnUrlPolicy.Sanitize(returnUrl));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/environments")]
    [InlineData("/environments?tab=2")]
    [InlineData("/requests/%2F%2Fevil")]
    public void A_local_path_is_kept(string returnUrl)
    {
        Assert.Equal(returnUrl, ReturnUrlPolicy.Sanitize(returnUrl));
    }

    [Fact]
    public void An_overlong_return_url_falls_back_to_home()
    {
        Assert.Equal(ReturnUrlPolicy.Fallback, ReturnUrlPolicy.Sanitize("/" + new string('a', 2048)));
    }

    [Fact]
    public void The_sign_in_path_escapes_the_return_path_and_carries_only_a_code()
    {
        Assert.Equal("/sign-in?error=invalid&ReturnUrl=%2Fworkspace%3Fx%3D1", ReturnUrlPolicy.SignInPath("/workspace?x=1", "invalid"));
    }
}
