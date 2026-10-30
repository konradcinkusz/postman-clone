using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Support;

/// <summary>
/// The tenant-isolation rule for id endpoints (CONTRACTS §0, README §4): a row that belongs to another workspace
/// answers <c>404 not_found</c>, exactly as a row that does not exist. Never <c>403</c>. Every id endpoint gets a
/// <c>Scoping_&lt;METHOD&gt;_&lt;route&gt;</c> test that calls this helper with a row of the other workspace.
/// </summary>
public static class ScopingAssertions
{
    /// <summary>
    /// Sends <paramref name="method"/> to <paramref name="url"/> with a token of another workspace and asserts the
    /// answer is <c>404</c> with code <c>not_found</c>. <paramref name="url"/> must address a row the caller's
    /// workspace does not own.
    /// </summary>
    public static async Task AssertNotFoundForOtherWorkspaceAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string otherWorkspaceToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherWorkspaceToken);
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await ProblemJson.CodeAsync(response, cancellationToken));
    }
}
