using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.EnvironmentsService.Api;
using PostmanClone.EnvironmentsService.Data;
using PostmanClone.EnvironmentsService.Environments;
using PostmanClone.EnvironmentsService.Secrets;
using PostmanClone.TestKit;
using Xunit;
using Variable = PostmanClone.EnvironmentsService.Domain.Variable;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsApiTests;
using static PostmanClone.EnvironmentsService.Tests.Environments.SecretFixtures;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsHost;

namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>
/// Secret variables: the keep rule, the flip rules, the all-or-nothing write, the missing-key path, and resolve
/// (CONTRACTS §3, ADR-0006). The database is read directly where a test asserts what is stored.
/// </summary>
public sealed class SecretRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_secret_is_stored_as_a_ciphertext_and_read_back_masked()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "secrets");

        using var response = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "secrets",
            variables = new[] { SecretVariable("API_TOKEN", Plaintext) },
        }, Ct);
        var dto = await response.Content.ReadFromJsonAsync<EnvironmentDto>(Ct);
        var stored = await StoredVariableAsync(host, environment.Id, "API_TOKEN");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var variable = Assert.Single(dto!.Variables);
        Assert.Null(variable.Value);
        Assert.True(variable.IsSecret);
        Assert.True(variable.HasValue);
        Assert.Null(stored.Value);
        Assert.StartsWith("v1:", stored.Ciphertext, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_without_a_value_keeps_the_stored_ciphertext_and_still_resolves_to_it()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "keep");
        using var _ = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "keep",
            variables = new[] { SecretVariable("API_TOKEN", Plaintext) },
        }, Ct);
        var before = (await StoredVariableAsync(host, environment.Id, "API_TOKEN")).Ciphertext;

        using var kept = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "keep renamed",
            variables = new[] { SecretVariable("API_TOKEN", null, keepExistingSecret: true) },
        }, Ct);
        var after = (await StoredVariableAsync(host, environment.Id, "API_TOKEN")).Ciphertext;
        using var resolved = await host.SendAsync(HttpMethod.Post, $"{EnvironmentsRoute}/{environment.Id}/resolve", caller, cancellationToken: Ct);
        var body = await resolved.Content.ReadFromJsonAsync<ResolvedEnvironment>(Ct);

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal(before, after);
        Assert.Equal(Plaintext, Assert.Single(body!.Variables).Value);
    }

    [Fact]
    public async Task Keeping_a_secret_that_is_not_stored_answers_400()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "keep");

        using var response = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "keep",
            variables = new[] { SecretVariable("NEW_TOKEN", null, keepExistingSecret: true) },
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("variables", await response.ErrorMembersAsync(Ct));
    }

    [Fact]
    public async Task Keeping_a_plain_variable_as_a_secret_answers_400()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "flip");
        using var _ = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "flip",
            variables = new[] { PlainVariable("TOKEN", "visible") },
        }, Ct);

        using var response = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "flip",
            variables = new[] { SecretVariable("TOKEN", null, keepExistingSecret: true) },
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_secret_with_no_value_and_no_keep_answers_400_validation_failed()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "secrets");

        using var response = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "secrets",
            variables = new[] { SecretVariable("API_TOKEN", null) },
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await response.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task A_secret_turned_plain_needs_a_value_and_then_stores_the_value_and_no_ciphertext()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "flip");
        using var _ = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "flip",
            variables = new[] { SecretVariable("TOKEN", Plaintext) },
        }, Ct);

        using var withoutValue = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "flip",
            variables = new[] { PlainVariable("TOKEN", null) },
        }, Ct);
        using var withValue = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "flip",
            variables = new[] { PlainVariable("TOKEN", "visible") },
        }, Ct);
        var stored = await StoredVariableAsync(host, environment.Id, "TOKEN");
        var read = await (await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{environment.Id}", caller, cancellationToken: Ct))
            .Content.ReadFromJsonAsync<EnvironmentDto>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, withoutValue.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withValue.StatusCode);
        Assert.False(stored.IsSecret);
        Assert.Equal("visible", stored.Value);
        Assert.Null(stored.Ciphertext);
        var variable = Assert.Single(read!.Variables);
        Assert.False(variable.IsSecret);
        Assert.Equal("visible", variable.Value);
    }

    [Fact]
    public async Task A_plain_variable_turned_secret_needs_a_value_and_then_stores_only_the_ciphertext()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "flip");
        using var _ = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "flip",
            variables = new[] { PlainVariable("TOKEN", "old") },
        }, Ct);

        using var keepWithoutStore = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "flip",
            variables = new[] { SecretVariable("TOKEN", null, keepExistingSecret: true) },
        }, Ct);
        using var withValue = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "flip",
            variables = new[] { SecretVariable("TOKEN", Plaintext) },
        }, Ct);
        var stored = await StoredVariableAsync(host, environment.Id, "TOKEN");

        Assert.Equal(HttpStatusCode.BadRequest, keepWithoutStore.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withValue.StatusCode);
        Assert.True(stored.IsSecret);
        Assert.Null(stored.Value);
        Assert.StartsWith("v1:", stored.Ciphertext, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_put_changes_nothing_stored()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "atomic");
        using var _ = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "atomic",
            variables = new[] { PlainVariable("A", "1"), SecretVariable("S", Plaintext) },
        }, Ct);

        // The plain change is valid on its own; the second write is not (no stored secret to keep).
        using var rejected = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "renamed",
            variables = new[] { PlainVariable("A", "2"), SecretVariable("Z", null, keepExistingSecret: true) },
        }, Ct);
        var read = await (await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{environment.Id}", caller, cancellationToken: Ct))
            .Content.ReadFromJsonAsync<EnvironmentDto>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("atomic", read!.Name);
        Assert.Equal(new[] { "A", "S" }, read.Variables.Select(variable => variable.Key).ToArray());
        Assert.Equal("1", read.Variables.Single(variable => variable.Key == "A").Value);
        Assert.True(read.Variables.Single(variable => variable.Key == "S").HasValue);
    }

    [Fact]
    public async Task A_new_secret_without_a_key_answers_503_secrets_unavailable_and_a_plain_write_still_works()
    {
        await using var host = await EnvironmentsHost.StartAsync(settings: KeylessSettings(), cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "no-key");

        using var secret = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "no-key",
            variables = new[] { SecretVariable("API_TOKEN", Plaintext) },
        }, Ct);
        using var plain = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "no-key",
            variables = new[] { PlainVariable("BASE", "https://example.test") },
        }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, secret.StatusCode);
        Assert.Equal("secrets_unavailable", await secret.ProblemCodeAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
    }

    [Fact]
    public async Task Keeping_a_stored_secret_needs_no_key_but_resolve_answers_503_without_one()
    {
        await using var host = await EnvironmentsHost.StartAsync(settings: KeylessSettings(), cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "stored");
        await WriteForeignSecretAsync(host, caller, environment.Id, "API_TOKEN", Plaintext);

        using var kept = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "stored",
            variables = new[] { SecretVariable("API_TOKEN", null, keepExistingSecret: true) },
        }, Ct);
        var read = await (await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{environment.Id}", caller, cancellationToken: Ct))
            .Content.ReadFromJsonAsync<EnvironmentDto>(Ct);
        using var resolved = await host.SendAsync(HttpMethod.Post, $"{EnvironmentsRoute}/{environment.Id}/resolve", caller, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.True(Assert.Single(read!.Variables).HasValue);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resolved.StatusCode);
        Assert.Equal("secrets_unavailable", await resolved.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task Resolve_without_any_secret_answers_200_even_without_a_key()
    {
        await using var host = await EnvironmentsHost.StartAsync(settings: KeylessSettings(), cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "plain-only");
        using var _ = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "plain-only",
            variables = new[] { PlainVariable("BASE", "https://example.test") },
        }, Ct);

        using var resolved = await host.SendAsync(HttpMethod.Post, $"{EnvironmentsRoute}/{environment.Id}/resolve", caller, cancellationToken: Ct);
        var body = await resolved.Content.ReadFromJsonAsync<ResolvedEnvironment>(Ct);

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal("https://example.test", Assert.Single(body!.Variables).Value);
    }

    [Fact]
    public async Task Resolve_returns_the_enabled_variables_in_key_order_with_each_secret_in_plaintext()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "resolve");
        using var _ = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "resolve",
            variables = new object[]
            {
                new { key = "SKIPPED", value = "off", isSecret = false, enabled = false, keepExistingSecret = false },
                PlainVariable("ALPHA", "1"),
                SecretVariable("ZULU", Plaintext),
            },
        }, Ct);

        using var response = await host.SendAsync(HttpMethod.Post, $"{EnvironmentsRoute}/{environment.Id}/resolve", caller, cancellationToken: Ct);
        var body = await response.Content.ReadFromJsonAsync<ResolvedEnvironment>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(environment.Id, body!.Id);
        Assert.Equal(
            new[] { ("ALPHA", "1", false), ("ZULU", Plaintext, true) },
            body.Variables.Select(variable => (variable.Key, variable.Value, variable.IsSecret)).ToArray());
    }

    [Fact]
    public async Task An_undecryptable_secret_answers_503_and_is_logged_without_its_value()
    {
        var logs = new CapturingLoggerProvider();
        await using var host = await EnvironmentsHost.StartAsync(logs: logs, cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var environment = await CreateAsync(host, caller, "corrupt");
        using var stored = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{environment.Id}", caller, new
        {
            name = "corrupt",
            variables = new[] { SecretVariable("API_TOKEN", Plaintext) },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);

        // The stored ciphertext is now one this host's key cannot open.
        await WriteForeignSecretAsync(host, caller, environment.Id, "API_TOKEN", Plaintext);

        using var response = await host.SendAsync(HttpMethod.Post, $"{EnvironmentsRoute}/{environment.Id}/resolve", caller, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("secrets_unavailable", await response.ProblemCodeAsync(Ct));
        Assert.Contains(logs.Lines, line => line.Contains("could not be decrypted", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(Plaintext, StringComparison.Ordinal));
    }

    /// <summary>The stored row of one variable, read straight from the database without tracking.</summary>
    private static async Task<Variable> StoredVariableAsync(EnvironmentsHost host, Guid environmentId, string key)
    {
        using var scope = host.Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EnvironmentsDbContext>();
        return await database.Variables.AsNoTracking()
            .SingleAsync(variable => variable.EnvironmentId == environmentId && variable.Key == key, Ct);
    }

    /// <summary>
    /// Writes a secret row directly, for what the API cannot produce on its own: a host without a key, or a row whose
    /// ciphertext does not match the host's key. The ciphertext is made under a random key of the test's own, so the
    /// host cannot decrypt it. The row is inserted when absent and its ciphertext replaced when present.
    /// </summary>
    private static async Task WriteForeignSecretAsync(EnvironmentsHost host, Caller caller, Guid environmentId, string key, string plaintext)
    {
        using var scope = host.Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EnvironmentsDbContext>();
        var place = SecretPlace.For(caller.Workspace, environmentId, key);
        var foreign = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(AesGcmSecretProtector.KeySizeInBytes));

        var row = await database.Variables.FirstOrDefaultAsync(variable => variable.EnvironmentId == environmentId && variable.Key == key, Ct);
        if (row is null)
        {
            database.Variables.Add(new Variable
            {
                Id = Guid.NewGuid(),
                WorkspaceId = caller.Workspace,
                EnvironmentId = environmentId,
                Key = key,
                IsSecret = true,
                Ciphertext = foreign.Protect(plaintext, place),
            });
        }
        else
        {
            row.Value = null;
            row.IsSecret = true;
            row.Ciphertext = foreign.Protect(plaintext, place);
        }

        await database.SaveChangesAsync(Ct);
    }
}
