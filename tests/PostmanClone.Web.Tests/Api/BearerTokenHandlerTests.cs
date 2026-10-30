using System.Net;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.Api;

/// <summary>The bearer travels in the request's options and is added by a stateless handler (BLAZOR-WEB-APP §7).</summary>
public sealed class BearerTokenHandlerTests
{
    private static async Task<string?> AuthorizationSeenByBackendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? seen = null;
        var backend = new FakeHttpMessageHandler(sent =>
        {
            seen = sent.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var invoker = new HttpMessageInvoker(new BearerTokenHandler { InnerHandler = backend });
        using var response = await invoker.SendAsync(request, cancellationToken);
        return seen;
    }

    [Fact]
    public async Task A_request_carrying_a_token_sends_it_as_the_bearer()
    {
        var request = AuthenticatedRequest.Create(HttpMethod.Get, "http://collections-service/api/v1/collections", "token-value");

        var seen = await AuthorizationSeenByBackendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("Bearer token-value", seen);
    }

    [Fact]
    public async Task A_request_without_a_token_carries_no_authorization_so_the_backend_refuses_it()
    {
        // The backend's own fallback policy refuses an anonymous call; the Web's [Authorize] is only UX (BLAZOR-WEB-APP §7).
        var request = AuthenticatedRequest.Create(HttpMethod.Get, "http://collections-service/api/v1/collections", accessToken: null);

        var seen = await AuthorizationSeenByBackendAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(seen);
    }
}
