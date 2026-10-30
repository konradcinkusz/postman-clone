using System.Net;
using System.Net.Http.Json;
using PostmanClone.EnvironmentsService.Api;
using Xunit;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsApiTests;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsHost;
using static PostmanClone.EnvironmentsService.Tests.Environments.SecretFixtures;

namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>
/// One scoping test per id endpoint (CONTRACTS §0, PROTOCOL §5). Two workspaces exist, the foreign caller is an Owner
/// of its own workspace, and it uses the id of a row that belongs to the other one. The answer is <c>404 not_found</c>
/// every time, never <c>403</c>, and the row is untouched.
/// </summary>
public sealed class ScopingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Scoping_GET_environments_id()
    {
        await using var fixture = await ForeignWorkspaceFixture.CreateAsync();

        using var foreign = await fixture.Host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{fixture.EnvironmentId}", fixture.Foreign, cancellationToken: Ct);
        var owner = await fixture.Host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{fixture.EnvironmentId}", fixture.Owner, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("not_found", await foreign.ProblemCodeAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task Scoping_PUT_environments_id()
    {
        await using var fixture = await ForeignWorkspaceFixture.CreateAsync();

        using var foreign = await fixture.Host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{fixture.EnvironmentId}", fixture.Foreign, new
        {
            name = "hijacked",
            variables = new[] { PlainVariable("BASE", "https://attacker.test") },
        }, Ct);
        var unchanged = await (await fixture.Host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{fixture.EnvironmentId}", fixture.Owner, cancellationToken: Ct))
            .Content.ReadFromJsonAsync<EnvironmentDto>(Ct);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("not_found", await foreign.ProblemCodeAsync(Ct));
        Assert.Equal("workspace-a", unchanged!.Name);
        Assert.Equal(new[] { "API_TOKEN", "BASE" }, unchanged.Variables.Select(variable => variable.Key).ToArray());
    }

    [Fact]
    public async Task Scoping_DELETE_environments_id()
    {
        await using var fixture = await ForeignWorkspaceFixture.CreateAsync();

        // The foreign caller is an Owner, so only the workspace check stands between it and the row.
        using var foreign = await fixture.Host.SendAsync(HttpMethod.Delete, $"{EnvironmentsRoute}/{fixture.EnvironmentId}", fixture.Foreign, cancellationToken: Ct);
        var owner = await fixture.Host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{fixture.EnvironmentId}", fixture.Owner, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("not_found", await foreign.ProblemCodeAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task Scoping_POST_environments_id_resolve()
    {
        await using var fixture = await ForeignWorkspaceFixture.CreateAsync();

        using var foreign = await fixture.Host.SendAsync(HttpMethod.Post, $"{EnvironmentsRoute}/{fixture.EnvironmentId}/resolve", fixture.Foreign, cancellationToken: Ct);
        var body = await foreign.Content.ReadAsStringAsync(Ct);
        using var owner = await fixture.Host.SendAsync(HttpMethod.Post, $"{EnvironmentsRoute}/{fixture.EnvironmentId}/resolve", fixture.Owner, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("not_found", await foreign.ProblemCodeAsync(Ct));
        Assert.DoesNotContain(Plaintext, body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task Environments_of_another_workspace_are_absent_from_the_list()
    {
        await using var fixture = await ForeignWorkspaceFixture.CreateAsync();

        var page = await (await fixture.Host.SendAsync(HttpMethod.Get, EnvironmentsRoute, fixture.Foreign, cancellationToken: Ct))
            .Content.ReadFromJsonAsync<Page<EnvironmentSummary>>(Ct);

        Assert.Empty(page!.Items);
    }

    /// <summary>
    /// Workspace A owns one environment with a plain variable and a secret. Workspace B has its own Owner and no rows.
    /// Both callers are in the same host, so the foreign caller really reaches the same database.
    /// </summary>
    private sealed class ForeignWorkspaceFixture : IAsyncDisposable
    {
        private ForeignWorkspaceFixture(EnvironmentsHost host, Guid environmentId, Caller owner, Caller foreign)
        {
            Host = host;
            EnvironmentId = environmentId;
            Owner = owner;
            Foreign = foreign;
        }

        public EnvironmentsHost Host { get; }

        public Guid EnvironmentId { get; }

        public Caller Owner { get; }

        public Caller Foreign { get; }

        public static async Task<ForeignWorkspaceFixture> CreateAsync()
        {
            var host = await EnvironmentsHost.StartAsync(cancellationToken: TestContext.Current.CancellationToken);
            var owner = Caller.Owner(Guid.NewGuid());
            var foreign = Caller.Owner(Guid.NewGuid(), "foreign-owner");
            var environment = await EnvironmentsApiTests.CreateAsync(host, owner, "workspace-a");

            using var stored = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
            {
                name = "workspace-a",
                variables = new object[] { SecretVariable("API_TOKEN", Plaintext), PlainVariable("BASE", "https://example.test") },
            }, TestContext.Current.CancellationToken);
            stored.EnsureSuccessStatusCode();

            return new ForeignWorkspaceFixture(host, environment.Id, owner, foreign);
        }

        public async ValueTask DisposeAsync() => await Host.DisposeAsync();
    }
}
