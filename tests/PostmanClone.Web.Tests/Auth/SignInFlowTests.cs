using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>
/// The sign-in flow on the real Web pipeline, against the fake authservice (WP12 acceptance). Each test is one
/// request sequence a browser would make; the cookie is handled by hand so each Set-Cookie can be inspected.
/// </summary>
public sealed class SignInFlowTests : IClassFixture<AuthHostFactory>
{
    private const string SessionCookie = "pc.session";
    private static readonly Regex JwtShape = new(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*");

    private readonly AuthHostFactory _factory;
    private readonly FakeAuthService _auth;

    public SignInFlowTests(AuthHostFactory factory)
    {
        _factory = factory;
        _auth = factory.AuthService;
        _auth.Reset();
    }

    private HttpClient Browser() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    private async Task<HttpResponseMessage> SignInAsync(HttpClient client, string password, string returnUrl = "", CancellationToken cancellationToken = default)
    {
        var (cookie, token) = await SignInForms.AntiforgeryAsync(client, "/sign-in", sessionCookie: null, cancellationToken);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["email"] = FakeAuthService.KnownEmail,
            ["password"] = password,
            ["returnUrl"] = returnUrl,
        };
        return await client.SendAsync(SignInForms.Post("/sign-in", fields, cookie), cancellationToken);
    }

    private async Task<HttpResponseMessage> SignedInAsync(HttpClient client, string path = "/", CancellationToken cancellationToken = default)
    {
        var response = await SignInAsync(client, FakeAuthService.KnownPassword, cancellationToken: cancellationToken);
        var cookie = SignInForms.SetCookie(response, SessionCookie);
        Assert.NotNull(cookie);
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(SignInForms.CookieHeaderName, $"{SessionCookie}={SignInForms.ValueOf(cookie)}");
        return await client.SendAsync(request, cancellationToken);
    }

    [Fact]
    public async Task A_successful_sign_in_sets_an_HttpOnly_strict_session_cookie_and_redirects_home()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Owner");
        using var client = Browser();

        using var response = await SignInAsync(client, FakeAuthService.KnownPassword, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        var cookie = SignInForms.SetCookie(response, SessionCookie);
        Assert.NotNull(cookie);
        Assert.Contains("; path=/", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        // Development over plain http: no Secure flag, and the unprefixed name (SessionCookie.RequiresSecure).
        Assert.DoesNotContain("secure", cookie.Split(';').Skip(1).Select(part => part.Trim()), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("private", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task The_session_cookie_is_a_random_id_and_no_token_is_in_it()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Owner");
        using var client = Browser();

        using var response = await SignInAsync(client, FakeAuthService.KnownPassword, cancellationToken: TestContext.Current.CancellationToken);

        var value = SignInForms.ValueOf(SignInForms.SetCookie(response, SessionCookie)!);
        Assert.Matches(@"^[A-Za-z0-9_-]{43}$", value);
        Assert.False(JwtShape.IsMatch(value), "the cookie must not carry a JWT");
        Assert.DoesNotContain(_auth.LastAccessToken!, value);
        Assert.DoesNotContain(_auth.LastRefreshToken!, value);
    }

    [Fact]
    public async Task A_wrong_password_gives_the_generic_message_and_sets_no_session()
    {
        using var client = Browser();

        using var response = await SignInAsync(client, "not-the-password", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/sign-in?error=invalid", response.Headers.Location?.OriginalString);
        Assert.Null(SignInForms.SetCookie(response, SessionCookie));

        using var page = await client.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Email or password is not right.", html);
        Assert.False(JwtShape.IsMatch(html), "a refused sign-in must not render any token");
    }

    [Fact]
    public async Task An_unreachable_authservice_says_so_and_sets_no_session()
    {
        _auth.Unavailable = true;
        using var client = Browser();

        using var response = await SignInAsync(client, FakeAuthService.KnownPassword, cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith("/sign-in?error=unavailable", response.Headers.Location?.OriginalString);
        Assert.Null(SignInForms.SetCookie(response, SessionCookie));
    }

    [Fact]
    public async Task A_two_factor_account_is_told_the_feature_is_not_supported_yet()
    {
        _auth.TwoFactorRequired = true;
        using var client = Browser();

        using var response = await SignInAsync(client, FakeAuthService.KnownPassword, cancellationToken: TestContext.Current.CancellationToken);

        Assert.StartsWith("/sign-in?error=two-factor", response.Headers.Location?.OriginalString);
        Assert.Null(SignInForms.SetCookie(response, SessionCookie));
    }

    [Theory]
    [InlineData("//evil.example/steal")]
    [InlineData("https://evil.example/steal")]
    [InlineData("/\\evil.example/steal")]
    [InlineData("/sign-in")]
    public async Task An_open_redirect_attempt_falls_back_to_home(string returnUrl)
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Owner");
        using var client = Browser();

        using var response = await SignInAsync(client, FakeAuthService.KnownPassword, returnUrl, TestContext.Current.CancellationToken);

        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_local_return_path_is_kept()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Owner");
        using var client = Browser();

        using var response = await SignInAsync(client, FakeAuthService.KnownPassword, "/environments?tab=2", TestContext.Current.CancellationToken);

        Assert.Equal("/environments?tab=2", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_sign_in_post_without_the_antiforgery_token_is_refused_and_never_reaches_authservice()
    {
        using var client = Browser();
        var (cookie, _) = await SignInForms.AntiforgeryAsync(client, "/sign-in", sessionCookie: null, TestContext.Current.CancellationToken);
        var fields = new Dictionary<string, string> { ["email"] = FakeAuthService.KnownEmail, ["password"] = FakeAuthService.KnownPassword };

        using var response = await client.SendAsync(SignInForms.Post("/sign-in", fields, cookie), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _auth.LoginCalls);
    }

    private async Task<HttpResponseMessage> RegisterAsync(HttpClient client, bool acceptTerms, bool acceptPrivacy, CancellationToken cancellationToken)
    {
        var (cookie, token) = await SignInForms.AntiforgeryAsync(client, "/register", sessionCookie: null, cancellationToken);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["email"] = "new-person@example.test",
            ["password"] = "new-password-1", // gitleaks:allow — a test fixture, not a credential.
        };
        if (acceptTerms)
        {
            fields["acceptTerms"] = "on";
        }

        if (acceptPrivacy)
        {
            fields["acceptPrivacy"] = "on";
        }

        return await client.SendAsync(SignInForms.Post("/register", fields, cookie), cancellationToken);
    }

    [Fact]
    public async Task A_registration_signs_the_new_user_in_when_no_confirmation_is_needed()
    {
        using var client = Browser();

        using var response = await RegisterAsync(client, acceptTerms: true, acceptPrivacy: true, TestContext.Current.CancellationToken);

        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.NotNull(SignInForms.SetCookie(response, SessionCookie));
        Assert.Equal(1, _auth.RegisterCalls);
    }

    [Fact]
    public async Task A_registration_that_needs_confirmation_says_so_and_sets_no_session()
    {
        _auth.RegistrationNeedsConfirmation = true;
        using var client = Browser();

        using var response = await RegisterAsync(client, acceptTerms: true, acceptPrivacy: true, TestContext.Current.CancellationToken);

        Assert.Equal("/sign-in?notice=verify", response.Headers.Location?.OriginalString);
        Assert.Null(SignInForms.SetCookie(response, SessionCookie));
        using var page = await client.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);
        Assert.Contains("Check your email to confirm the address", await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_registration_without_both_consents_is_refused_before_authservice_is_asked()
    {
        using var client = Browser();

        using var response = await RegisterAsync(client, acceptTerms: true, acceptPrivacy: false, TestContext.Current.CancellationToken);

        Assert.StartsWith("/register?error=details", response.Headers.Location?.OriginalString);
        Assert.Equal(0, _auth.RegisterCalls);
        Assert.Null(SignInForms.SetCookie(response, SessionCookie));
    }

    [Fact]
    public async Task A_protected_page_without_a_session_redirects_to_sign_in_with_the_return_path()
    {
        using var client = Browser();

        using var response = await client.GetAsync("/workspace", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?ReturnUrl=%2Fworkspace", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task The_signed_in_page_carries_no_token_in_its_markup()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Owner");
        using var client = Browser();

        using var response = await SignedInAsync(client, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("signed in as Owner", html);
        Assert.DoesNotContain(_auth.LastAccessToken!, html);
        Assert.DoesNotContain(_auth.LastRefreshToken!, html);
        Assert.False(JwtShape.IsMatch(html), "no JWT may be rendered into the page or its persisted state");
    }

    [Fact]
    public async Task Sign_out_revokes_at_authservice_clears_the_cookie_and_ends_the_session()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Owner");
        using var client = Browser();
        using var signedIn = await SignInAsync(client, FakeAuthService.KnownPassword, cancellationToken: TestContext.Current.CancellationToken);
        var sessionValue = SignInForms.ValueOf(SignInForms.SetCookie(signedIn, SessionCookie)!);
        var accessToken = _auth.LastAccessToken;
        var sessionCookie = $"{SessionCookie}={sessionValue}";
        var (antiforgeryCookie, token) = await SignInForms.AntiforgeryAsync(client, "/", sessionCookie, TestContext.Current.CancellationToken);

        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        var request = SignInForms.Post("/sign-out", fields, $"{antiforgeryCookie}; {sessionCookie}");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in", response.Headers.Location?.OriginalString);
        Assert.Equal(1, _auth.LogoutCalls);
        Assert.Equal(accessToken, _auth.LastLogoutBearer);
        var cleared = SignInForms.SetCookie(response, SessionCookie);
        Assert.NotNull(cleared);
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());

        // The old cookie no longer opens a session.
        var after = new HttpRequestMessage(HttpMethod.Get, "/workspace");
        after.Headers.TryAddWithoutValidation(SignInForms.CookieHeaderName, $"{SessionCookie}={sessionValue}");
        using var afterResponse = await client.SendAsync(after, TestContext.Current.CancellationToken);
        Assert.StartsWith("/sign-in", afterResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_user_with_no_workspace_is_offered_the_create_form_and_gets_the_new_claim_after_it()
    {
        using var client = Browser();
        var signIn = await SignInAsync(client, FakeAuthService.KnownPassword, cancellationToken: TestContext.Current.CancellationToken);
        var sessionValue = SignInForms.ValueOf(SignInForms.SetCookie(signIn, SessionCookie)!);
        var sessionCookie = $"{SessionCookie}={sessionValue}";
        using var explained = await client.SendAsync(SignInForms.Get("/no-workspace", sessionCookie), TestContext.Current.CancellationToken);
        Assert.Contains("data-testid=\"create-workspace-form\"", await explained.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var (antiforgeryCookie, token) = await SignInForms.AntiforgeryAsync(client, "/no-workspace", sessionCookie, TestContext.Current.CancellationToken);
        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["name"] = "Team" };
        using var created = await client.SendAsync(
            SignInForms.Post("/no-workspace/create", fields, $"{antiforgeryCookie}; {sessionCookie}"),
            TestContext.Current.CancellationToken);

        Assert.Equal("/", created.Headers.Location?.OriginalString);
        Assert.Equal(1, _auth.RefreshCalls);
        using var home = await client.SendAsync(SignInForms.Get("/", sessionCookie), TestContext.Current.CancellationToken);
        Assert.Contains("signed in as Owner", await home.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_user_with_two_workspaces_sees_both_by_name_and_that_switching_is_not_supported()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Owner");
        _auth.Join(Guid.NewGuid(), "Globex", "Member");
        using var client = Browser();

        using var page = await SignedInAsync(client, "/no-workspace", TestContext.Current.CancellationToken);

        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("data-testid=\"workspace-multiple\"", html);
        Assert.Contains("Acme (Owner)", html);
        Assert.Contains("Globex (Member)", html);
        Assert.Contains("support switching between them yet", html);
    }

    [Fact]
    public async Task A_user_with_two_workspaces_is_routed_to_the_explanation_not_to_a_domain_page()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Owner");
        _auth.Join(Guid.NewGuid(), "Globex", "Member");
        using var client = Browser();

        using var response = await SignedInAsync(client, "/environments", TestContext.Current.CancellationToken);

        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("data-testid=\"workspace-redirecting\"", html);
        Assert.DoesNotContain("data-testid=\"empty-state\"", html);
    }
}
