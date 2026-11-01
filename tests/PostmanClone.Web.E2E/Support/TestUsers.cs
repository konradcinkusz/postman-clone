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
    // authservice on the local stack sometimes takes 20 to 50 s for one register (seen in the sessions: idle it takes 0.1 s, and a
    // burst of twelve had one at 31 s). The timeout is long enough to wait it out, and a stalled attempt is retried with a new address.
    private static readonly HttpClient Http = new() { BaseAddress = E2ESettings.AuthBaseUrl, Timeout = TimeSpan.FromSeconds(60) };
    private const int Attempts = 3;

    /// <summary>
    /// Registers a unique user. With <paramref name="organizations"/> &gt; 0, it also creates that many organizations,
    /// so the user holds that many memberships: 1 is a normal workspace; 2 is the Multiple page (ADR-0004).
    /// </summary>
    public static async Task<TestUser> CreateAsync(int organizations = 1)
    {
        var (terms, privacy) = await ConsentVersionsAsync();
        string email = string.Empty, password = string.Empty, accessToken = string.Empty, refreshToken = string.Empty;
        for (var attempt = 1; ; attempt++)
        {
            email = $"e2e-{Guid.NewGuid():N}@example.test";
            password = $"Pw-{Guid.NewGuid():N}-Aa1"; // a generated value, never stored in the tree
            await AuthBudget.AcquireAsync(TestContext.Current.CancellationToken);
            try
            {
                var register = await Http.PostAsJsonAsync("/api/v1/auth/register", new
                {
                    email,
                    password,
                    acceptedTermsVersion = terms,
                    acceptedPrivacyVersion = privacy,
                }, TestContext.Current.CancellationToken);
                register.EnsureSuccessStatusCode();
                var tokens = await register.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
                accessToken = tokens.GetProperty("accessToken").GetString()!;
                refreshToken = tokens.GetProperty("refreshToken").GetString()!;
                break;
            }
            catch (Exception exception) when (attempt < Attempts && exception is HttpRequestException or TaskCanceledException)
            {
                // A stalled attempt may still have created its user; the retry uses a new address, so nothing is reused.
                // Logged to the test output: a retry is visible, never a silent pass.
                TestContext.Current.SendDiagnosticMessage(
                    $"authservice register attempt {attempt} of {Attempts} failed ({exception.GetType().Name}); retrying with a new address.");
            }
        }

        for (var index = 0; index < organizations; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/organizations")
            {
                Content = JsonContent.Create(new { name = $"E2E Org {Guid.NewGuid():N}" }),
            };
            request.Headers.Authorization = new("Bearer", accessToken);
            using var created = await Http.SendAsync(request, TestContext.Current.CancellationToken);
            created.EnsureSuccessStatusCode();
        }

        // The register token has no organization claim, so the workspace API refuses it (403). When the user has an organization,
        // the token is refreshed (docs/AUTH.md, step 5), and the refreshed access token is the one returned.
        if (organizations > 0)
        {
            await AuthBudget.AcquireAsync(TestContext.Current.CancellationToken);
            using var refresh = await Http.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken }, TestContext.Current.CancellationToken);
            refresh.EnsureSuccessStatusCode();
            var pair = await refresh.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
            accessToken = pair.GetProperty("accessToken").GetString()!;
        }

        return new TestUser(email, password, accessToken);
    }

    private static async Task<(string Terms, string Privacy)> ConsentVersionsAsync()
    {
        var versions = await Http.GetFromJsonAsync<JsonElement>("/api/v1/auth/consents/versions", TestContext.Current.CancellationToken);
        return (versions.GetProperty("terms").GetString()!, versions.GetProperty("privacy").GetString()!);
    }
}
