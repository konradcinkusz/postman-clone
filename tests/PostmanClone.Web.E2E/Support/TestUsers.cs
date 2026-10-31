using System.Net.Http.Json;
using System.Text.Json;

namespace PostmanClone.Web.E2E.Support;

/// <summary>A user created for one journey. Its address and its organization are unique per run (a GUID, no date).</summary>
public sealed record TestUser(string Email, string Password, string AccessToken);

/// <summary>
/// Creates users through authservice's own API, the same calls the Web makes (docs/AUTH.md). A journey that does not test
/// the register form uses this to start from a known account; it still signs in through the form.
/// </summary>
public static class TestUsers
{
    private static readonly HttpClient Http = new() { BaseAddress = E2ESettings.AuthBaseUrl, Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// Registers a unique user. With <paramref name="organizations"/> &gt; 0, it also creates that many organizations,
    /// so the user holds that many memberships: 1 is a normal workspace; 2 is the Multiple page (ADR-0004).
    /// </summary>
    public static async Task<TestUser> CreateAsync(int organizations = 1)
    {
        var (terms, privacy) = await ConsentVersionsAsync();
        var email = $"e2e-{Guid.NewGuid():N}@example.test";
        var password = $"Pw-{Guid.NewGuid():N}-Aa1"; // a generated value, never stored in the tree

        var register = await Http.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password,
            acceptedTermsVersion = terms,
            acceptedPrivacyVersion = privacy,
        });
        register.EnsureSuccessStatusCode();
        var tokens = await register.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokens.GetProperty("accessToken").GetString()!;

        for (var index = 0; index < organizations; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/organizations")
            {
                Content = JsonContent.Create(new { name = $"E2E Org {Guid.NewGuid():N}" }),
            };
            request.Headers.Authorization = new("Bearer", accessToken);
            using var created = await Http.SendAsync(request);
            created.EnsureSuccessStatusCode();
        }

        return new TestUser(email, password, accessToken);
    }

    private static async Task<(string Terms, string Privacy)> ConsentVersionsAsync()
    {
        var versions = await Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/consents/versions");
        return (versions.GetProperty("terms").GetString()!, versions.GetProperty("privacy").GetString()!);
    }
}
