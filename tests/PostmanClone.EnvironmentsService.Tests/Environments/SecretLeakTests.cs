using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.EnvironmentsService.Api;
using PostmanClone.EnvironmentsService.Data;
using PostmanClone.ServiceDefaults.Database;
using PostmanClone.TestKit;
using Xunit;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsApiTests;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsHost;
using static PostmanClone.EnvironmentsService.Tests.Environments.SecretFixtures;

namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>
/// The secret must not leave the service except through <c>resolve</c>: not in a response, not in a database column,
/// not in a log line (ADR-0006, CONTRACTS §3). Every test here uses a secret whose plaintext it searches for.
/// </summary>
public sealed class SecretLeakTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The routes the response sweep covers. <c>resolve</c> is excluded because it returns plaintext by design.</summary>
    private static readonly string[] SweptRoutes =
    [
        "/alive",
        "/api/v1/environments",
        "/api/v1/environments/{id:guid}",
        "/health",
    ];

    [Fact]
    public async Task Response_sweep_no_endpoint_returns_the_plaintext_after_a_secret_is_created()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var owner = Caller.Owner(Guid.NewGuid());
        var foreign = Caller.Owner(Guid.NewGuid(), "foreign-owner");
        var environment = await CreateAsync(host, owner, "sweep");
        var transcript = new List<string>();

        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
        {
            name = "sweep",
            variables = new[] { SecretVariable("API_TOKEN", Plaintext), PlainVariable("BASE", "https://example.test") },
        }, Ct)));
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Post, EnvironmentsRoute, owner, new { name = "sweep-two" }, Ct)));
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Get, EnvironmentsRoute, owner, cancellationToken: Ct)));
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{environment.Id}", owner, cancellationToken: Ct)));
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
        {
            name = "sweep renamed",
            variables = new[] { SecretVariable("API_TOKEN", null, keepExistingSecret: true), PlainVariable("BASE", "https://example.test") },
        }, Ct)));

        // Rejected writes that carry the plaintext: the answers must not echo it.
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
        {
            name = "sweep",
            variables = new[] { PlainVariable("bad key", Plaintext) },
        }, Ct)));
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
        {
            name = "sweep",
            variables = new[] { PlainVariable("SAME", Plaintext), PlainVariable("SAME", Plaintext) },
        }, Ct)));
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
        {
            name = "sweep",
            variables = new[] { SecretVariable("NEW_TOKEN", null, keepExistingSecret: true), PlainVariable("NOTE", Plaintext) },
        }, Ct)));
        transcript.Add(await Transcript(await SendRawAsync(host, owner, $"{{\"name\": \"{Plaintext}\", \"broken\":")));

        // A foreign workspace: every answer is the same 404, and none carries the plaintext.
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{environment.Id}", foreign, cancellationToken: Ct)));
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", foreign, new
        {
            name = "hijacked",
            variables = new[] { SecretVariable("API_TOKEN", Plaintext) },
        }, Ct)));
        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Delete, $"{EnvironmentsRoute}/{environment.Id}", foreign, cancellationToken: Ct)));

        transcript.Add(await Transcript(await host.SendAsync(HttpMethod.Delete, $"{EnvironmentsRoute}/{environment.Id}", owner, cancellationToken: Ct)));
        transcript.Add(await Transcript(await host.Client.GetAsync("/health", Ct)));
        transcript.Add(await Transcript(await host.Client.GetAsync("/alive", Ct)));

        // The count guards against a silently skipped step; the plaintext must be absent from every answer.
        Assert.Equal(15, transcript.Count);
        Assert.DoesNotContain(Plaintext, string.Join("\n", transcript), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sweep_covers_every_route_except_resolve()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var routes = host.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => "/" + endpoint.RoutePattern.RawText!.Trim('/'))
            .Distinct()
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();

        // A new route fails this test until the sweep names it, so no endpoint can leak outside the sweep unnoticed.
        // Both resolve routes are checked by their own tests: the user route and the agent-only route (CONTRACTS §3, A2.4).
        var expected = SweptRoutes
            .Append($"{EnvironmentsRoute}/{{id:guid}}/resolve")
            .Append("/api/v1/agent/environments/{id:guid}/resolve")
            .Append("/api/v1/agent/installations/{id:guid}")
            .Append("/api/v1/agent/installations")
            .Append("/api/v1/agent/me")
            .Append("/api/v1/agent/pair")
            .Append("/api/v1/agent/pairing-codes")
            .OrderBy(route => route, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, routes);
    }

    [Fact]
    public async Task Database_rows_hold_ciphertext_only_for_a_secret_in_memory()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var owner = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, owner, "rows");
        using var stored = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
        {
            name = "rows",
            variables = new[] { SecretVariable("API_TOKEN", Plaintext), PlainVariable("BASE", "visible-plain") },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);

        using var scope = host.Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EnvironmentsDbContext>();
        var environments = await database.Environments.AsNoTracking().Where(row => row.WorkspaceId == owner.Workspace).ToListAsync(Ct);
        var variables = await database.Variables.AsNoTracking().Where(row => row.WorkspaceId == owner.Workspace).ToListAsync(Ct);

        var everyColumn = environments.Cast<object>().Concat(variables).SelectMany(ColumnValues).ToArray();
        Assert.DoesNotContain(everyColumn, value => value.Contains(Plaintext, StringComparison.Ordinal));

        var secret = Assert.Single(variables, row => row.Key == "API_TOKEN");
        Assert.Null(secret.Value);
        Assert.StartsWith("v1:", secret.Ciphertext, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_rows_hold_ciphertext_only_for_a_secret_in_postgresql()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL row check needs a server.");
        }

        var settings = KeyedSettings();
        settings[DatabaseExtensions.ProviderSetting] = DatabaseExtensions.PostgreSqlProvider;
        settings["ConnectionStrings:environmentsdb"] = postgres.ConnectionString;
        await using var host = await EnvironmentsHost.StartAsync(settings: settings, cancellationToken: Ct);
        var owner = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, owner, "rows");
        using var stored = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
        {
            name = "rows",
            variables = new[] { SecretVariable("API_TOKEN", Plaintext), PlainVariable("BASE", "visible-plain") },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);

        using var scope = host.Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EnvironmentsDbContext>();
        // Every column of every row, as the database holds it: a plaintext in any column would show here.
        var rows = (await database.Database.SqlQueryRaw<string>("SELECT row_to_json(v)::text FROM \"Variables\" v").ToListAsync(Ct))
            .Concat(await database.Database.SqlQueryRaw<string>("SELECT row_to_json(e)::text FROM \"Environments\" e").ToListAsync(Ct))
            .ToArray();

        Assert.Equal(3, rows.Length);
        Assert.DoesNotContain(rows, row => row.Contains(Plaintext, StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("v1:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Log_capture_shows_no_variable_value_across_create_update_reject_and_resolve()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await EnvironmentsHost.StartAsync(logs: logs, cancellationToken: Ct);
        var owner = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, owner, "logged");

        using var updated = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
        {
            name = "logged",
            variables = new[] { SecretVariable("API_TOKEN", Plaintext), PlainVariable("BASE", "https://example.test") },
        }, Ct);
        using var rejected = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", owner, new
        {
            name = "logged",
            variables = new[] { PlainVariable("bad key", Plaintext) },
        }, Ct);
        using var fetched = await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{environment.Id}", owner, cancellationToken: Ct);
        using var resolved = await host.SendAsync(HttpMethod.Post, $"{EnvironmentsRoute}/{environment.Id}/resolve", owner, cancellationToken: Ct);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        // The audit line is there, so the absence checks below cannot pass by logging nothing.
        Assert.Contains(logs.Lines, line => line.Contains(
            $"Resolved environment {environment.Id} for user {owner.UserId}: 2 variables.", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(Plaintext, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains("https://example.test", StringComparison.Ordinal));
    }

    /// <summary>Every string the row holds, so a check sees each column rather than the ones a test thought of.</summary>
    private static IEnumerable<string> ColumnValues(object row) => row.GetType()
        .GetProperties()
        .Where(property => property.PropertyType == typeof(string) && property.GetIndexParameters().Length == 0)
        .Select(property => property.GetValue(row) as string)
        .Where(value => value is not null)
        .Cast<string>();

    /// <summary>The status, the headers and the body of one answer, as text, for a search of the whole answer.</summary>
    private static async Task<string> Transcript(HttpResponseMessage response)
    {
        var headers = response.Headers.Concat(response.Content.Headers)
            .Select(header => $"{header.Key}: {string.Join(",", header.Value)}");
        return $"{(int)response.StatusCode}\n{string.Join("\n", headers)}\n{await response.Content.ReadAsStringAsync(Ct)}";
    }

    private static async Task<HttpResponseMessage> SendRawAsync(EnvironmentsHost host, Caller caller, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, EnvironmentsRoute)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", host.TokenFor(caller)) },
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await host.Client.SendAsync(request, Ct);
    }
}
