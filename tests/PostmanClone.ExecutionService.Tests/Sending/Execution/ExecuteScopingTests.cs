using System.Net;
using Microsoft.AspNetCore.Http;
using PostmanClone.ExecutionService.Api;
using PostmanClone.ExecutionService.Tests.Support;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Execution;

/// <summary>Tenant isolation for the sender (CONTRACTS §0): an environment of another workspace is not found.</summary>
public sealed class ExecuteScopingTests
{
    [Fact]
    public async Task Scoping_POST_execute()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("reached"));
        var environmentId = environments.Add(owner, ("baseUrl", target.Address.AbsoluteUri, false));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));

        var intruder = await Sender.ForAsync(factory, stranger, TestContext.Current.CancellationToken);
        using var refused = await intruder.PostAsync(
            Requests.Execute("GET", "{{baseUrl}}items", environmentId: environmentId),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Contains("\"code\":\"not_found\"", await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(target.Requests);

        // The same environment is found, and used, for its own workspace.
        var member = await Sender.ForAsync(factory, owner, TestContext.Current.CancellationToken);
        var result = await member.ExecuteAsync(
            Requests.Execute("GET", "{{baseUrl}}items", environmentId: environmentId),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        Assert.Equal($"{target.Address}items", result.FinalUrl);
        Assert.Single(target.Requests);
    }
}
