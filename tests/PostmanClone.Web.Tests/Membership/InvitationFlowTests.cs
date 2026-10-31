using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PostmanClone.Web.Tests.Auth;

namespace PostmanClone.Web.Tests.Membership;

/// <summary>
/// The invitation forms on the real Web pipeline, against the fake authservice (WP24 acceptance). Each test is one request
/// sequence a browser would make: sign in, fetch a form for its antiforgery token, post it, and read where it redirects.
/// The redirects carry codes, not tokens, and the tests check both.
/// </summary>
public sealed class InvitationFlowTests : IClassFixture<AuthHostFactory>
{
    private const string SessionCookie = "pc.session";

    private readonly AuthHostFactory _factory;
    private readonly FakeAuthService _auth;

    public InvitationFlowTests(AuthHostFactory factory)
    {
        _factory = factory;
        _auth = factory.AuthService;
        _auth.Reset();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private HttpClient Browser() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    /// <summary>Signs the known address in and returns its session cookie as <c>name=value</c>.</summary>
    private async Task<string> SignInAsync(HttpClient client)
    {
        var (cookie, token) = await SignInForms.AntiforgeryAsync(client, "/sign-in", sessionCookie: null, Cancellation);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["email"] = FakeAuthService.KnownEmail,
            ["password"] = FakeAuthService.KnownPassword,
            ["returnUrl"] = string.Empty,
        };
        using var response = await client.SendAsync(SignInForms.Post("/sign-in", fields, cookie), Cancellation);
        var setCookie = SignInForms.SetCookie(response, SessionCookie);
        Assert.NotNull(setCookie);
        return $"{SessionCookie}={SignInForms.ValueOf(setCookie)}";
    }

    /// <summary>Posts a form as a browser would: the antiforgery pair from the page it came from, plus the session cookie.</summary>
    private async Task<HttpResponseMessage> PostAsync(HttpClient client, string session, string path, string page, IReadOnlyDictionary<string, string> fields)
    {
        var (antiforgery, token) = await SignInForms.AntiforgeryAsync(client, page, session, Cancellation);
        var all = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        return await client.SendAsync(SignInForms.Post(path, all, $"{antiforgery}; {session}"), Cancellation);
    }

    private static string LocationOf(HttpResponseMessage response) => response.Headers.Location?.OriginalString ?? string.Empty;

    private async Task<HttpResponseMessage> InviteAsync(HttpClient client, string session, string email, string role = "Member") =>
        await PostAsync(client, session, "/workspace/invitations", "/workspace", new Dictionary<string, string> { ["email"] = email, ["role"] = role });

    private async Task<HttpResponseMessage> AcceptAsync(HttpClient client, string session, string token) =>
        await PostAsync(client, session, "/accept-invitation", $"/accept-invitation?token={token}", new Dictionary<string, string> { ["token"] = token });

    [Fact]
    public async Task A_member_is_refused_an_invite_before_authservice_is_asked()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Member");
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await InviteAsync(client, session, "new@example.test");

        Assert.Equal("/workspace?error=forbidden", LocationOf(response));
        Assert.Equal(0, _auth.InviteCalls);
    }

    [Fact]
    public async Task An_admin_invite_is_sent_and_the_redirect_says_so()
    {
        var organization = Guid.NewGuid();
        _auth.Join(organization, "Acme", "Admin");
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await InviteAsync(client, session, "new@example.test");

        Assert.Equal("/workspace?notice=invited", LocationOf(response));
        Assert.Equal(1, _auth.InviteCalls);
        Assert.Equal(organization.ToString(), _auth.LastInviteOrganization);
        Assert.False(string.IsNullOrEmpty(_auth.LastInviteBearer));
    }

    [Fact]
    public async Task An_invalid_address_is_refused_without_asking_authservice()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Admin");
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await InviteAsync(client, session, "not-an-address");

        Assert.Equal("/workspace?error=invalid", LocationOf(response));
        Assert.Equal(0, _auth.InviteCalls);
    }

    [Fact]
    public async Task An_unknown_role_is_refused_without_asking_authservice()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Admin");
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await InviteAsync(client, session, "new@example.test", role: "Owner");

        Assert.Equal("/workspace?error=invalid", LocationOf(response));
        Assert.Equal(0, _auth.InviteCalls);
    }

    [Fact]
    public async Task Inviting_someone_already_in_the_workspace_says_so()
    {
        var organization = Guid.NewGuid();
        _auth.Join(organization, "Acme", "Admin");
        _auth.AddMember(organization, "member@example.test", "Member");
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await InviteAsync(client, session, "member@example.test");

        Assert.Equal("/workspace?error=member", LocationOf(response));
    }

    [Fact]
    public async Task A_second_invite_to_a_pending_address_says_so()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Admin");
        using var client = Browser();
        var session = await SignInAsync(client);
        using var first = await InviteAsync(client, session, "new@example.test");
        Assert.Equal("/workspace?notice=invited", LocationOf(first));

        using var second = await InviteAsync(client, session, "new@example.test");

        Assert.Equal("/workspace?error=pending", LocationOf(second));
    }

    [Fact]
    public async Task An_admin_whose_role_was_taken_away_is_refused_by_authservice()
    {
        var organization = Guid.NewGuid();
        _auth.Join(organization, "Acme", "Admin");
        using var client = Browser();
        var session = await SignInAsync(client);
        // The session still says Admin until it refreshes; authservice has already changed the role.
        _auth.Join(organization, "Acme", "Member");

        using var response = await InviteAsync(client, session, "new@example.test");

        Assert.Equal("/workspace?error=forbidden", LocationOf(response));
        Assert.Equal(1, _auth.InviteCalls);
    }

    [Fact]
    public async Task Scoping_POST_workspace_invitations_takes_the_workspace_from_the_token_not_the_form()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        _auth.Join(mine, "Acme", "Admin");
        // Another organization the signed-in address is not in: a second membership would route the account to the Multiple page.
        _auth.AddMember(other, "owner@example.test", "Owner");
        using var client = Browser();
        var session = await SignInAsync(client);
        var fields = new Dictionary<string, string>
        {
            ["email"] = "new@example.test",
            ["role"] = "Member",
            ["organization"] = other.ToString(),
        };

        using var response = await PostAsync(client, session, "/workspace/invitations", "/workspace", fields);

        Assert.Equal("/workspace?notice=invited", LocationOf(response));
        Assert.Equal(mine.ToString(), _auth.LastInviteOrganization);
    }

    [Fact]
    public async Task Accepting_an_invitation_joins_the_workspace_and_refreshes_the_session()
    {
        using var client = Browser();
        var session = await SignInAsync(client);
        var organization = Guid.NewGuid();
        var token = _auth.SeedInvitation(organization, FakeAuthService.KnownEmail, "Member");
        var refreshesBefore = _auth.RefreshCalls;

        using var response = await AcceptAsync(client, session, token);

        Assert.Equal("/workspace?notice=joined", LocationOf(response));
        Assert.Equal("Member", _auth.RoleIn(organization));
        // The new organization claim is read only after a refresh: exactly one, made at once.
        Assert.Equal(refreshesBefore + 1, _auth.RefreshCalls);
    }

    [Fact]
    public async Task Accepting_an_invitation_to_a_workspace_already_held_says_so_and_joins_nothing_new()
    {
        var organization = Guid.NewGuid();
        _auth.Join(organization, "Acme", "Member");
        using var client = Browser();
        var session = await SignInAsync(client);
        var token = _auth.SeedInvitation(organization, FakeAuthService.KnownEmail, "Admin");

        using var response = await AcceptAsync(client, session, token);

        Assert.Equal("/workspace?notice=already", LocationOf(response));
        Assert.Equal("Member", _auth.RoleIn(organization));
    }

    [Fact]
    public async Task An_expired_invitation_says_so_on_the_accept_page()
    {
        using var client = Browser();
        var session = await SignInAsync(client);
        var token = _auth.SeedInvitation(Guid.NewGuid(), FakeAuthService.KnownEmail, "Member", expired: true);

        using var response = await AcceptAsync(client, session, token);

        Assert.Equal("/accept-invitation?error=expired", LocationOf(response));
        Assert.DoesNotContain(token, LocationOf(response));
    }

    [Fact]
    public async Task An_invitation_that_was_already_used_says_so()
    {
        using var client = Browser();
        var session = await SignInAsync(client);
        var token = _auth.SeedInvitation(Guid.NewGuid(), FakeAuthService.KnownEmail, "Member", accepted: true);

        using var response = await AcceptAsync(client, session, token);

        Assert.Equal("/accept-invitation?error=used", LocationOf(response));
    }

    [Fact]
    public async Task An_invitation_sent_to_another_address_is_not_found_for_this_one()
    {
        using var client = Browser();
        var session = await SignInAsync(client);
        var token = _auth.SeedInvitation(Guid.NewGuid(), "someone-else@example.test", "Member");

        using var response = await AcceptAsync(client, session, token);

        Assert.Equal("/accept-invitation?error=notfound", LocationOf(response));
        Assert.Equal(1, _auth.AcceptCalls);
    }

    [Fact]
    public async Task An_unverified_address_cannot_accept()
    {
        using var client = Browser();
        var session = await SignInAsync(client);
        var token = _auth.SeedInvitation(Guid.NewGuid(), FakeAuthService.KnownEmail, "Member");
        _auth.EmailNotVerified = true;

        using var response = await AcceptAsync(client, session, token);

        Assert.Equal("/accept-invitation?error=verify", LocationOf(response));
    }

    [Fact]
    public async Task A_post_without_a_token_is_refused_without_asking_authservice()
    {
        using var client = Browser();
        var session = await SignInAsync(client);

        using var response = await PostAsync(client, session, "/accept-invitation", "/accept-invitation", new Dictionary<string, string> { ["token"] = " " });

        Assert.Equal("/accept-invitation?error=missing", LocationOf(response));
        Assert.Equal(0, _auth.AcceptCalls);
    }

    [Fact]
    public async Task Someone_with_no_workspace_can_reach_the_accept_page_to_get_one()
    {
        using var client = Browser();
        var session = await SignInAsync(client);
        var token = _auth.SeedInvitation(Guid.NewGuid(), FakeAuthService.KnownEmail, "Member");

        using var response = await client.SendAsync(SignInForms.Get($"/accept-invitation?token={token}", session), Cancellation);

        // Not sent to /no-workspace first: the accept page is the way out of having no workspace.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("data-testid=\"accept-form\"", html);
    }

    [Fact]
    public async Task A_visit_to_the_accept_page_without_a_session_keeps_the_token_through_sign_in()
    {
        using var client = Browser();

        using var response = await client.GetAsync("/accept-invitation?token=abc-DEF_123", Cancellation);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/sign-in?ReturnUrl=%2Faccept-invitation%3Ftoken%3Dabc-DEF_123", LocationOf(response));
    }

    [Fact]
    public async Task Accepting_a_second_workspace_leaves_the_account_with_two_and_the_multiple_page_explains_it()
    {
        _auth.Join(Guid.NewGuid(), "Acme", "Member");
        using var client = Browser();
        var session = await SignInAsync(client);
        var token = _auth.SeedInvitation(Guid.NewGuid(), FakeAuthService.KnownEmail, "Member");

        using var accepted = await AcceptAsync(client, session, token);
        Assert.Equal("/workspace?notice=joined", LocationOf(accepted));

        // The workspace page sends a two-workspace account to the explanation; that page names the situation.
        using var workspace = await client.SendAsync(SignInForms.Get("/workspace", session), Cancellation);
        var layout = await workspace.Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("data-testid=\"workspace-redirecting\"", layout);

        using var explanation = await client.SendAsync(SignInForms.Get("/no-workspace", session), Cancellation);
        var html = await explanation.Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("data-testid=\"workspace-multiple\"", html);
    }
}
