using System.Net.Http.Headers;

namespace PostmanClone.Web.Api;

/// <summary>
/// Builds requests that carry a bearer token. The token travels in the request's <see cref="HttpRequestOptions"/>, not
/// in a field of a scoped service, so the handler below can run in the HttpClient's own scope (BLAZOR-WEB-APP §7).
/// </summary>
public static class AuthenticatedRequest
{
    public static readonly HttpRequestOptionsKey<string> BearerToken = new("PostmanClone.BearerToken");

    /// <summary>A request with the given bearer. With a null token the request carries none, and the backend refuses it.</summary>
    public static HttpRequestMessage Create(HttpMethod method, string path, string? accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Options.Set(BearerToken, accessToken);
        }

        return request;
    }
}

/// <summary>
/// Puts the bearer from the request's options into the <c>Authorization</c> header. Stateless: it holds no token and no
/// scoped service, so one instance serves every circuit. Registered on every backend client (CONTRACTS §5).
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(AuthenticatedRequest.BearerToken, out var token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
