using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PostmanClone.Web.Api;

/// <summary>What an authservice call came back with, in the terms the Web acts on.</summary>
public enum AuthOutcome
{
    /// <summary>A 2xx answer with the expected body.</summary>
    Succeeded,

    /// <summary>A 4xx answer: wrong credentials, a used refresh token, a rejected form. The reason is never shown.</summary>
    Rejected,

    /// <summary>Registration accepted, but the address must be confirmed first (<c>202</c>, no tokens).</summary>
    PendingVerification,

    /// <summary>The account has two-factor sign-in enabled. The Web does not complete it yet.</summary>
    TwoFactorRequired,

    /// <summary>No usable answer: a transport failure, a timeout or a 5xx.</summary>
    Unavailable,
}

/// <summary>An outcome and, when it succeeded, the body it carried.</summary>
public sealed record AuthAnswer<T>(AuthOutcome Outcome, T? Value);

/// <summary>
/// An organization call's answer. On success <see cref="Value"/> holds the body. On failure <see cref="Status"/> (0 when
/// there was no answer) and <see cref="Error"/>, the <c>error</c> text authservice gave, let the caller say why
/// (docs/AUTH.md, the invitation rows). Only the membership rules read the text.
/// </summary>
public sealed record OrganizationAnswer<T>(AuthOutcome Outcome, int Status, T? Value, string? Error);

/// <summary>
/// The Web's client for authservice (CONTRACTS §6, docs/AUTH.md). Each call names the bearer it needs through
/// <see cref="AuthenticatedRequest"/>; the client never keeps a token of its own. Only the callers in
/// <c>Auth/</c> hold the tokens, on the server.
/// </summary>
public sealed class AuthServiceClient(IHttpClientFactory clients)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AuthAnswer<TokenResponse>> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        using var request = JsonRequest(HttpMethod.Post, "/api/v1/auth/login", new LoginRequest(email, password));
        using var response = await SendAsync(request, cancellationToken);
        if (response is null)
        {
            return new AuthAnswer<TokenResponse>(AuthOutcome.Unavailable, null);
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return new AuthAnswer<TokenResponse>(FromStatus(response.StatusCode), null);
        }

        // A two-factor account gets a 200 with a challenge instead of a token pair (docs/AUTH.md, row 5).
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (body.RootElement.TryGetProperty("requiresTwoFactor", out var challenge) && challenge.GetBoolean())
        {
            return new AuthAnswer<TokenResponse>(AuthOutcome.TwoFactorRequired, null);
        }

        return ReadTokens(body.RootElement);
    }

    public async Task<AuthAnswer<TokenResponse>> RegisterAsync(string email, string password, string termsVersion, string privacyVersion, CancellationToken cancellationToken)
    {
        using var request = JsonRequest(HttpMethod.Post, "/api/v1/auth/register", new RegisterRequest(email, password, termsVersion, privacyVersion));
        using var response = await SendAsync(request, cancellationToken);
        if (response is null)
        {
            return new AuthAnswer<TokenResponse>(AuthOutcome.Unavailable, null);
        }

        // 202 means the address must be confirmed first. Without a configured sender authservice answers 200 with tokens.
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            return new AuthAnswer<TokenResponse>(AuthOutcome.PendingVerification, null);
        }

        return response.StatusCode == HttpStatusCode.OK
            ? await ReadTokensAsync(response, cancellationToken)
            : new AuthAnswer<TokenResponse>(FromStatus(response.StatusCode), null);
    }

    /// <summary>Exchanges a refresh token. The old one is spent whatever the answer (rotation, single use).</summary>
    public async Task<AuthAnswer<TokenResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = JsonRequest(HttpMethod.Post, "/api/v1/auth/refresh", new RefreshTokenRequest(refreshToken));
        using var response = await SendAsync(request, cancellationToken);
        if (response is null)
        {
            return new AuthAnswer<TokenResponse>(AuthOutcome.Unavailable, null);
        }

        return response.StatusCode == HttpStatusCode.OK
            ? await ReadTokensAsync(response, cancellationToken)
            : new AuthAnswer<TokenResponse>(FromStatus(response.StatusCode), null);
    }

    /// <summary>Revokes every refresh token of the user (docs/AUTH.md, row 6). A bearer is required.</summary>
    public async Task<AuthOutcome> LogoutAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = AuthenticatedRequest.Create(HttpMethod.Post, "/api/v1/auth/logout", accessToken);
        using var response = await SendAsync(request, cancellationToken);
        return response is null ? AuthOutcome.Unavailable : FromStatus(response.StatusCode);
    }

    public async Task<UserInfoResponse?> MeAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = AuthenticatedRequest.Create(HttpMethod.Get, "/api/v1/auth/me", accessToken);
        using var response = await SendAsync(request, cancellationToken);
        return response?.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<UserInfoResponse>(Json, cancellationToken)
            : null;
    }

    /// <summary>The consent versions a registration must quote exactly (docs/AUTH.md, row 2). Public.</summary>
    public async Task<ConsentVersionsResponse?> ConsentVersionsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/consents/versions");
        using var response = await SendAsync(request, cancellationToken);
        return response?.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<ConsentVersionsResponse>(Json, cancellationToken)
            : null;
    }

    /// <summary>Creates an organization; its creator becomes Owner (docs/AUTH.md, row 4). The caller refreshes to see it.</summary>
    public async Task<AuthOutcome> CreateOrganizationAsync(string accessToken, string name, CancellationToken cancellationToken)
    {
        using var request = AuthenticatedRequest.Create(HttpMethod.Post, "/api/v1/organizations", accessToken);
        request.Content = JsonContent.Create(new CreateOrganizationRequest(name), options: Json);
        using var response = await SendAsync(request, cancellationToken);
        return response is null ? AuthOutcome.Unavailable : FromStatus(response.StatusCode);
    }

    /// <summary>The organization a member sees: its name and members (docs/AUTH.md, the endpoints table). Any member may read it.</summary>
    public Task<OrganizationAnswer<OrganizationDetail>> GetOrganizationAsync(string accessToken, string organizationId, CancellationToken cancellationToken) =>
        SendOrganizationAsync<OrganizationDetail>(AuthenticatedRequest.Create(HttpMethod.Get, $"/api/v1/organizations/{organizationId}", accessToken), cancellationToken);

    /// <summary>Invites an address to the organization. authservice refuses a caller below Admin and sends or logs the email itself.</summary>
    public Task<OrganizationAnswer<InvitationSummary>> InviteMemberAsync(string accessToken, string organizationId, string email, string role, CancellationToken cancellationToken)
    {
        var request = AuthenticatedRequest.Create(HttpMethod.Post, $"/api/v1/organizations/{organizationId}/invite", accessToken);
        request.Content = JsonContent.Create(new InviteMemberRequest(email, role), options: Json);
        return SendOrganizationAsync<InvitationSummary>(request, cancellationToken);
    }

    /// <summary>The pending invitations of the organization. Owners and Admins only; authservice answers 403 to anyone else.</summary>
    public Task<OrganizationAnswer<List<InvitationSummary>>> ListInvitationsAsync(string accessToken, string organizationId, CancellationToken cancellationToken) =>
        SendOrganizationAsync<List<InvitationSummary>>(AuthenticatedRequest.Create(HttpMethod.Get, $"/api/v1/organizations/{organizationId}/invitations", accessToken), cancellationToken);

    /// <summary>
    /// Accepts an invitation for the signed-in address. The token must name an invitation to that address; an invitation
    /// to someone else answers 404, the same as an unknown token (docs/AUTH.md).
    /// </summary>
    public Task<OrganizationAnswer<AcceptedInvitation>> AcceptInvitationAsync(string accessToken, string token, CancellationToken cancellationToken)
    {
        var request = AuthenticatedRequest.Create(HttpMethod.Post, "/api/v1/organizations/invitations/accept", accessToken);
        request.Content = JsonContent.Create(new AcceptInvitationRequest(token), options: Json);
        return SendOrganizationAsync<AcceptedInvitation>(request, cancellationToken);
    }

    private static HttpRequestMessage JsonRequest<T>(HttpMethod method, string path, T body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        return request;
    }

    /// <summary>Sends the request to authservice. Null when there was no answer: a transport failure or a timeout.</summary>
    private async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await clients.CreateClient(BackendServices.AuthServiceName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout from the resilience handler, not the caller's cancellation.
            return null;
        }
    }

    private async Task<OrganizationAnswer<T>> SendOrganizationAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken);
        if (response is null)
        {
            return new OrganizationAnswer<T>(AuthOutcome.Unavailable, 0, default, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await ErrorTextAsync(response, cancellationToken);
            return new OrganizationAnswer<T>(FromStatus(response.StatusCode), (int)response.StatusCode, default, error);
        }

        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
            return value is null
                ? new OrganizationAnswer<T>(AuthOutcome.Unavailable, (int)response.StatusCode, default, null)
                : new OrganizationAnswer<T>(AuthOutcome.Succeeded, (int)response.StatusCode, value, null);
        }
        catch (JsonException)
        {
            // A 2xx with a body the Web cannot read is no usable answer, the same as a 5xx.
            return new OrganizationAnswer<T>(AuthOutcome.Unavailable, (int)response.StatusCode, default, null);
        }
    }

    /// <summary>The <c>error</c> text of a failed answer, or null when the body has none (a bare 403, a validation problem).</summary>
    private static async Task<string?> ErrorTextAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<AuthErrorBody>(Json, cancellationToken))?.Error;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task<AuthAnswer<TokenResponse>> ReadTokensAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        Succeeded(await response.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken));

    private AuthAnswer<TokenResponse> ReadTokens(JsonElement body) => Succeeded(body.Deserialize<TokenResponse>(Json));

    /// <summary>A pair with both tokens is a success; anything else is no usable answer.</summary>
    private static AuthAnswer<TokenResponse> Succeeded(TokenResponse? tokens) =>
        tokens is { AccessToken.Length: > 0, RefreshToken.Length: > 0 }
            ? new AuthAnswer<TokenResponse>(AuthOutcome.Succeeded, tokens)
            : new AuthAnswer<TokenResponse>(AuthOutcome.Unavailable, null);

    private static AuthOutcome FromStatus(HttpStatusCode status) => (int)status switch
    {
        >= 200 and < 300 => AuthOutcome.Succeeded,
        >= 400 and < 500 => AuthOutcome.Rejected,
        _ => AuthOutcome.Unavailable,
    };
}
