using System.Text.Json;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.Api;

/// <summary>
/// The Web's copies of authservice's shapes, read from the JSON authservice v0.3.4 sends (docs/AUTH.md, the DTOs in
/// AuthDtos.cs). The Web cannot reference authservice's types, so this pins the names and the types instead.
/// </summary>
public sealed class AuthDtoTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_token_response_reads_camel_case_names_and_an_integer_lifetime()
    {
        const string body = """{"accessToken":"a","refreshToken":"r","expiresIn":3600,"tokenType":"Bearer"}""";

        var tokens = JsonSerializer.Deserialize<TokenResponse>(body, Json);

        Assert.Equal("a", tokens!.AccessToken);
        Assert.Equal("r", tokens.RefreshToken);
        Assert.Equal(3600, tokens.ExpiresIn);
        Assert.Equal("Bearer", tokens.TokenType);
    }

    [Fact]
    public void The_consent_versions_read_all_three_documents()
    {
        const string body = """{"terms":"terms-v1","privacy":"privacy-v1","cookies":"cookies-v1"}""";

        var versions = JsonSerializer.Deserialize<ConsentVersionsResponse>(body, Json);

        Assert.Equal(("terms-v1", "privacy-v1", "cookies-v1"), (versions!.Terms, versions.Privacy, versions.Cookies));
    }

    [Fact]
    public void The_profile_reads_its_organization_memberships_and_ignores_the_rest()
    {
        const string body = """
            {"id":"u","email":"person@example.test","userName":null,"createdAt":"2000-01-01T00:00:00Z",
             "organizations":[{"id":"o1","name":"Acme","imageUrl":null,"role":"Owner"},{"id":"o2","name":"Globex","imageUrl":null,"role":"Member"}],
             "hasPassword":true,"requiresConsent":false}
            """;

        var profile = JsonSerializer.Deserialize<UserInfoResponse>(body, Json);

        Assert.Equal("person@example.test", profile!.Email);
        Assert.Equal(["Acme (Owner)", "Globex (Member)"], profile.Organizations.Select(org => $"{org.Name} ({org.Role})"));
    }

    [Fact]
    public void A_registration_is_written_with_the_names_authservice_expects()
    {
        var body = JsonSerializer.Serialize(new RegisterRequest("person@example.test", "pw", "terms-v1", "privacy-v1"), Json);

        Assert.Contains("\"acceptedTermsVersion\":\"terms-v1\"", body);
        Assert.Contains("\"acceptedPrivacyVersion\":\"privacy-v1\"", body);
    }
    [Fact]
    public void The_organization_reads_its_members_and_ignores_the_dates_and_images()
    {
        const string body = """
            {"id":"o1","name":"Acme","description":null,"imageUrl":null,"createdAt":"2000-01-01T00:00:00Z",
             "members":[{"userId":"u1","email":"owner@example.test","userName":"owner","profileImageUrl":null,"role":"Owner","joinedAt":"2000-01-01T00:00:00Z"}]}
            """;

        var organization = JsonSerializer.Deserialize<OrganizationDetail>(body, Json);

        Assert.Equal(("o1", "Acme"), (organization!.Id, organization.Name));
        Assert.Equal(["owner@example.test (Owner)"], organization.Members.Select(member => $"{member.Email} ({member.Role})"));
    }

    [Fact]
    public void A_pending_invitation_reads_its_address_role_and_email_status_and_drops_the_token()
    {
        const string body = """
            [{"id":"i1","organizationId":"o1","organizationName":"Acme","email":"invitee@example.test","role":"Admin",
              "createdAt":"2000-01-01T00:00:00Z","expiresAt":"2000-01-08T00:00:00Z","isAccepted":false,"emailStatus":"Sent",
              "emailAttemptCount":1,"lastEmailAttemptAt":null,"nextRetryAt":null,"lastEmailError":null,"token":"secret-token"}]
            """;

        var invitations = JsonSerializer.Deserialize<List<InvitationSummary>>(body, Json);

        Assert.Equal(("invitee@example.test", "Admin", "Sent"), (invitations![0].Email, invitations[0].Role, invitations[0].EmailStatus));
        Assert.DoesNotContain("secret-token", JsonSerializer.Serialize(invitations, Json));
    }

    [Fact]
    public void The_accept_answer_names_the_organization_it_joined()
    {
        const string body = """{"message":"Invitation accepted successfully","organizationId":"o9"}""";

        var accepted = JsonSerializer.Deserialize<AcceptedInvitation>(body, Json);

        Assert.Equal("o9", accepted!.OrganizationId);
    }

    [Fact]
    public void The_invite_and_accept_requests_are_camel_case()
    {
        var invite = JsonSerializer.Serialize(new InviteMemberRequest("person@example.test", "Admin"), Json);
        var accept = JsonSerializer.Serialize(new AcceptInvitationRequest("t"), Json);

        Assert.Equal("""{"email":"person@example.test","role":"Admin"}""", invite);
        Assert.Equal("""{"token":"t"}""", accept);
    }

    [Fact]
    public void An_authservice_error_body_gives_its_text()
    {
        const string body = """{"error":"Invitation has expired"}""";

        Assert.Equal("Invitation has expired", JsonSerializer.Deserialize<AuthErrorBody>(body, Json)!.Error);
    }
}
