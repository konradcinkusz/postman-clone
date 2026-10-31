using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PostmanClone.ExecutionService.Api;
using PostmanClone.ExecutionService.Data;
using PostmanClone.ExecutionService.Sending.Assembly;
using PostmanClone.ExecutionService.Tests.Support;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.History;

/// <summary>
/// What the history keeps: masked text only, never a secret value in any form (CONTRACTS §4, ADR-0006, ADR-0015). The
/// assertions read the stored rows, not the API, so a leak into a column is caught even when the API would hide it.
/// </summary>
public sealed class HistoryMaskingTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Sensitive_header_and_query_values_are_masked_in_the_stored_template_and_url()
    {
        var workspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("reached"));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var caller = await HistoryCaller.ForAsync(factory, workspace, user, TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var result = await caller.ExecuteResultAsync(
            new
            {
                request = HistoryCaller.Get(
                    $"{target.Address}items?token=literal-query-token&page=2",
                    headers: new object[]
                    {
                        new { key = "Authorization", value = "Bearer literal-bearer-token" },
                        new { key = "X-Api-Key", value = "literal-key-value" },
                        new { key = "Accept", value = "application/json" },
                    }),
            },
            ct);

        var entry = await caller.EntryAsync(result.HistoryId!.Value, ct);
        Assert.Equal($"{target.Address}items?token=••••••&page=2", entry.Request.Url);
        var headers = entry.Request.Headers.ToDictionary(header => header.Key, header => header.Value);
        Assert.Equal(SecretMasker.Mask, headers["Authorization"]);
        Assert.Equal(SecretMasker.Mask, headers["X-Api-Key"]);
        Assert.Equal("application/json", headers["Accept"]);

        foreach (var text in await StoredTextAsync(factory, result.HistoryId!.Value, ct))
        {
            Assert.DoesNotContain("literal-query-token", text);
            Assert.DoesNotContain("literal-bearer-token", text);
            Assert.DoesNotContain("literal-key-value", text);
        }
    }

    [Fact]
    public async Task A_resolved_secret_typed_as_a_literal_is_absent_from_every_column_and_every_answer()
    {
        var secret = SecretForms.Fresh("sec");
        var workspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(EchoEverything(secret));
        var environmentId = environments.Add(workspace, ("baseUrl", target.Address.AbsoluteUri, false), ("apiKey", secret, true));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var caller = await HistoryCaller.ForAsync(factory, workspace, user, TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var result = await caller.ExecuteResultAsync(SecretRequest(environmentId, secret), ct);

        Assert.Equal(ExecutionOutcome.Completed, result.Outcome);
        AssertAbsent(JsonSerializer.Serialize(result), secret, "the answer to the caller");
        var historyId = result.HistoryId!.Value;
        AssertAbsent(JsonSerializer.Serialize(await caller.EntryAsync(historyId, ct)), secret, "the history entry");
        AssertAbsent(JsonSerializer.Serialize(await caller.ListPageAsync("", ct)), secret, "the history list");

        var rows = await StoredTextAsync(factory, historyId, ct);
        Assert.NotEmpty(rows);
        foreach (var text in rows)
        {
            AssertAbsent(text, secret, "a stored column");
        }
    }

    [Fact]
    public async Task A_resolved_secret_is_absent_from_every_column_of_every_row_in_PostgreSQL()
    {
        if (!postgres.Enabled)
        {
            Assert.Skip("TEST_POSTGRES is not set; the PostgreSQL column check needs a server.");
        }

        var secret = SecretForms.Fresh("pgsec");
        var workspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(EchoEverything(secret));
        var environmentId = environments.Add(workspace, ("baseUrl", target.Address.AbsoluteUri, false), ("apiKey", secret, true));
        await using var factory = new ExecutionServiceFactory("Development", new Dictionary<string, string>(SenderSettings.Loopback(environments.Server.Address))
        {
            ["DATABASE_PROVIDER"] = "PostgreSQL",
            ["ConnectionStrings:executiondb"] = postgres.ConnectionString,
        });
        var caller = await HistoryCaller.ForAsync(factory, workspace, user, TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        // A second entry from the same user keeps the check honest about more than one row.
        var first = await caller.ExecuteResultAsync(SecretRequest(environmentId, secret), ct);
        var second = await caller.ExecuteResultAsync(SecretRequest(environmentId, secret), ct);
        Assert.NotNull(first.HistoryId);
        Assert.NotNull(second.HistoryId);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT row_to_json(h)::text FROM history_entries AS h", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(reader.GetString(0));
        }

        Assert.Equal(2, rows.Count);
        foreach (var row in rows)
        {
            AssertAbsent(row, secret, "a PostgreSQL row");
        }
    }

    [Fact]
    public async Task Auth_helper_values_are_not_stored_only_its_type_and_placement()
    {
        var secret = SecretForms.Fresh("tok");
        var workspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context => context.Response.WriteAsync("reached"));
        var environmentId = environments.Add(workspace, ("baseUrl", target.Address.AbsoluteUri, false), ("tokenRef", secret, true), ("pwRef", secret + "-pw", true));
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var caller = await HistoryCaller.ForAsync(factory, workspace, user, TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var bearer = await caller.ExecuteResultAsync(
            new
            {
                request = HistoryCaller.Get("{{baseUrl}}bearer", auth: new { type = "bearer", bearerToken = "{{tokenRef}}" }),
                environmentId,
            },
            ct);
        var basic = await caller.ExecuteResultAsync(
            new
            {
                request = HistoryCaller.Get("{{baseUrl}}basic", auth: new { type = "basic", username = "literal-user-name", password = "{{pwRef}}" }),
                environmentId,
            },
            ct);

        var bearerEntry = await caller.EntryAsync(bearer.HistoryId!.Value, ct);
        Assert.Equal(AuthType.Bearer, bearerEntry.Request.Auth.Type);
        Assert.Null(bearerEntry.Request.Auth.BearerToken);

        var basicEntry = await caller.EntryAsync(basic.HistoryId!.Value, ct);
        Assert.Equal(AuthType.Basic, basicEntry.Request.Auth.Type);
        Assert.Null(basicEntry.Request.Auth.Username);
        Assert.Null(basicEntry.Request.Auth.Password);

        foreach (var id in new[] { bearer.HistoryId!.Value, basic.HistoryId!.Value })
        {
            foreach (var text in await StoredTextAsync(factory, id, ct))
            {
                Assert.DoesNotContain("tokenRef", text);
                Assert.DoesNotContain("pwRef", text);
                Assert.DoesNotContain("literal-user-name", text);
            }
        }
    }

    [Fact]
    public async Task The_stored_body_is_capped_at_64_KB_and_marked_truncated_while_the_caller_keeps_the_full_body()
    {
        const int bodyBytes = 100 * 1024;
        const int historyLimit = 64 * 1024;
        var workspace = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var environments = await FakeEnvironments.StartAsync();
        await using var target = await LoopbackServer.StartAsync(context =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync(new string('x', bodyBytes));
        });
        await using var factory = new ExecutionServiceFactory(overrides: SenderSettings.Loopback(environments.Server.Address));
        var caller = await HistoryCaller.ForAsync(factory, workspace, user, TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        var result = await caller.ExecuteResultAsync(new { request = HistoryCaller.Get($"{target.Address}big") }, ct);

        Assert.Equal(bodyBytes, result.Response!.Body.Content.Length);
        Assert.False(result.Response.Body.Truncated);

        var stored = await caller.EntryAsync(result.HistoryId!.Value, ct);
        Assert.True(Encoding.UTF8.GetByteCount(stored.Result.Response!.Body.Content) <= historyLimit);
        Assert.True(stored.Result.Response.Body.Truncated);
        Assert.Equal(bodyBytes, stored.Result.Response.Body.SizeBytes);
    }

    /// <summary>A request that carries the secret everywhere it could appear: a reference, and the literal typed in the URL, a header and the body.</summary>
    private static object SecretRequest(Guid environmentId, string secret) => new
    {
        request = new
        {
            method = "POST",
            url = $"{{{{baseUrl}}}}items?q={{{{apiKey}}}}&echo={secret}",
            @params = Array.Empty<object>(),
            headers = new object[]
            {
                new { key = "X-Trace", value = secret },
                new { key = "X-Ref", value = "{{apiKey}}" },
            },
            body = new { type = "raw", rawLanguage = "json", raw = $"{{\"literal\":\"{secret}\",\"ref\":\"{{{{apiKey}}}}\"}}", fields = Array.Empty<object>() },
            auth = new { type = "none" },
        },
        environmentId,
    };

    /// <summary>The target repeats the secret in its answer, in its body, in a header and in base64, so the masked answer is tested too.</summary>
    private static Func<HttpContext, Task> EchoEverything(string secret) => context =>
    {
        context.Response.Headers["X-Echo"] = secret;
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
        return context.Response.WriteAsync($"query={context.Request.QueryString}; trace={context.Request.Headers["X-Trace"]}; base64={base64}; raw={secret}");
    };

    private static void AssertAbsent(string text, string secret, string where)
    {
        var found = SecretForms.FindIn(text, secret);
        Assert.True(found is null, $"A secret form ({found?.Length ?? 0} characters) appears in {where}.");
    }

    /// <summary>The text of every stored column of one entry, read through the entity, so no column is left out.</summary>
    private static async Task<IReadOnlyList<string>> StoredTextAsync(ExecutionServiceFactory factory, Guid id, CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var row = await db.HistoryEntries.AsNoTracking().SingleAsync(entry => entry.Id == id, cancellationToken);
        return TextColumns(row);
    }

    private static IReadOnlyList<string> TextColumns(object row) => row.GetType()
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(string))
        .Select(property => (string)property.GetValue(row)!)
        .ToList();
}
