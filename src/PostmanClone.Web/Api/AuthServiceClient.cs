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
