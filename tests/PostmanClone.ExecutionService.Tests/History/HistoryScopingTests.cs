using System.Net;
using PostmanClone.ExecutionService.Tests.Support;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.History;

/// <summary>
/// Tenant isolation for the history (CONTRACTS §0, ADR-0015). Each test has three callers built from the same ids: the owner;
/// another user in the owner's workspace; and a stranger. The stranger is shown in two forms: a different user in another
/// workspace, and the owner's own user id in another workspace. A row of anyone else is <c>404</c>, never <c>403</c>.
/// </summary>
public sealed class HistoryScopingTests
{
    [Fact]
    public async Task Scoping_GET_history()
    {
        var workspace = Guid.NewGuid();
        var otherWorkspace = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory();
        await HistorySeed.AddAsync(factory, workspace, owner, "https://a.test/one", TestContext.Current.CancellationToken);
        await HistorySeed.AddAsync(factory, workspace, owner, "https://a.test/two", TestContext.Current.CancellationToken);
        var callers = await CreateCallers(factory, workspace, otherWorkspace, owner, sibling, stranger);

        Assert.Equal(2, (await callers.Owner.ListPageAsync("", TestContext.Current.CancellationToken)).Total);
        Assert.Equal(0, (await callers.Sibling.ListPageAsync("", TestContext.Current.CancellationToken)).Total);
        Assert.Equal(0, (await callers.Stranger.ListPageAsync("", TestContext.Current.CancellationToken)).Total);
        Assert.Equal(0, (await callers.SameUserOtherWorkspace.ListPageAsync("", TestContext.Current.CancellationToken)).Total);
    }

    [Fact]
    public async Task Scoping_GET_history_id()
    {
        var workspace = Guid.NewGuid();
        var otherWorkspace = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory();
        var id = await HistorySeed.AddAsync(factory, workspace, owner, "https://a.test/private", TestContext.Current.CancellationToken);
        var callers = await CreateCallers(factory, workspace, otherWorkspace, owner, sibling, stranger);

        Assert.Equal("https://a.test/private", (await callers.Owner.EntryAsync(id, TestContext.Current.CancellationToken)).Request.Url);

        using (var fromSibling = await callers.Sibling.GetAsync(id, TestContext.Current.CancellationToken))
        {
            await HistoryCaller.AssertNotFoundAsync(fromSibling);
        }

        using (var fromStranger = await callers.Stranger.GetAsync(id, TestContext.Current.CancellationToken))
        {
            await HistoryCaller.AssertNotFoundAsync(fromStranger);
        }

        using (var sameUser = await callers.SameUserOtherWorkspace.GetAsync(id, TestContext.Current.CancellationToken))
        {
            await HistoryCaller.AssertNotFoundAsync(sameUser);
        }
    }

    [Fact]
    public async Task Scoping_DELETE_history_id()
    {
        var workspace = Guid.NewGuid();
        var otherWorkspace = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory();
        var id = await HistorySeed.AddAsync(factory, workspace, owner, "https://a.test/kept", TestContext.Current.CancellationToken);
        var callers = await CreateCallers(factory, workspace, otherWorkspace, owner, sibling, stranger);

        using (var fromSibling = await callers.Sibling.DeleteAsync(id, TestContext.Current.CancellationToken))
        {
            await HistoryCaller.AssertNotFoundAsync(fromSibling);
        }

        using (var fromStranger = await callers.Stranger.DeleteAsync(id, TestContext.Current.CancellationToken))
        {
            await HistoryCaller.AssertNotFoundAsync(fromStranger);
        }

        using (var sameUser = await callers.SameUserOtherWorkspace.DeleteAsync(id, TestContext.Current.CancellationToken))
        {
            await HistoryCaller.AssertNotFoundAsync(sameUser);
        }

        // The refusals deleted nothing: the owner still reads the entry.
        Assert.Equal(id, (await callers.Owner.EntryAsync(id, TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task Scoping_DELETE_history()
    {
        var workspace = Guid.NewGuid();
        var otherWorkspace = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var sibling = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await using var factory = new ExecutionServiceFactory();
        await HistorySeed.AddAsync(factory, workspace, owner, "https://a.test/one", TestContext.Current.CancellationToken);
        await HistorySeed.AddAsync(factory, workspace, owner, "https://a.test/two", TestContext.Current.CancellationToken);
        var callers = await CreateCallers(factory, workspace, otherWorkspace, owner, sibling, stranger);

        // Clearing as another user, or as the same user in another workspace, answers 204 and clears nothing of the owner's.
        using (var fromSibling = await callers.Sibling.ClearAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, fromSibling.StatusCode);
        }

        using (var sameUser = await callers.SameUserOtherWorkspace.ClearAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, sameUser.StatusCode);
        }

        Assert.Equal(2, (await callers.Owner.ListPageAsync("", TestContext.Current.CancellationToken)).Total);

        using (var own = await callers.Owner.ClearAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        }

        Assert.Equal(0, (await callers.Owner.ListPageAsync("", TestContext.Current.CancellationToken)).Total);
    }

    private static async Task<Callers> CreateCallers(
        ExecutionServiceFactory factory, Guid workspace, Guid otherWorkspace, Guid owner, Guid sibling, Guid stranger)
    {
        var ct = TestContext.Current.CancellationToken;
        return new Callers(
            Owner: await HistoryCaller.ForAsync(factory, workspace, owner, ct),
            Sibling: await HistoryCaller.ForAsync(factory, workspace, sibling, ct),
            Stranger: await HistoryCaller.ForAsync(factory, otherWorkspace, stranger, ct),
            SameUserOtherWorkspace: await HistoryCaller.ForAsync(factory, otherWorkspace, owner, ct));
    }

    private sealed record Callers(HistoryCaller Owner, HistoryCaller Sibling, HistoryCaller Stranger, HistoryCaller SameUserOtherWorkspace);
}
