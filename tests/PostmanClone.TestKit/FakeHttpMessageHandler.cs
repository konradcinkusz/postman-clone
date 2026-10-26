namespace PostmanClone.TestKit;

/// <summary>
/// Stands in for the network as an <see cref="HttpMessageHandler"/>: records each request and answers with
/// the response the test supplies. Use it as a configured primary handler, never as a real server.
/// </summary>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly List<HttpRequestMessage> _requests = [];

    public IReadOnlyList<HttpRequestMessage> Requests => _requests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
