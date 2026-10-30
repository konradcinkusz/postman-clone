using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using PostmanClone.ServiceDefaults.Integrations;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.EnvironmentsService.Tests.Health;

/// <summary>The secret state is an optional integration (P8): it is reported, never the reason for a 503.</summary>
public sealed class HealthTests
{
    private const string KeySetting = "Secrets:EncryptionKey";

    [Fact]
    public async Task Health_reports_secrets_available_when_a_valid_key_is_configured()
    {
        using var tokens = new TestTokens();
        await using var factory = new EnvironmentsServiceFactory(tokens, new Dictionary<string, string?>
        {
            [KeySetting] = NewKey(),
        });
        await factory.WaitForSchemaAsync(TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", body!["status"]);
        Assert.Equal(OptionalIntegrationStatus.Available, body["secrets"]);
    }

    [Fact]
    public async Task Health_reports_secrets_unavailable_and_still_answers_200_when_no_key_is_configured()
    {
        using var tokens = new TestTokens();
        await using var factory = new EnvironmentsServiceFactory(tokens, new Dictionary<string, string?>());
        await factory.WaitForSchemaAsync(TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", body!["status"]);
        Assert.Equal(OptionalIntegrationStatus.Unavailable, body["secrets"]);
    }

    [Fact]
    public async Task Liveness_answers_anonymously()
    {
        using var tokens = new TestTokens();
        await using var factory = new EnvironmentsServiceFactory(tokens, new Dictionary<string, string?>());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_key_is_never_written_to_the_logs_or_the_health_payload()
    {
        var key = NewKey();
        var logs = new CapturingLoggerProvider();
        using var tokens = new TestTokens();
        await using var factory = new EnvironmentsServiceFactory(tokens, new Dictionary<string, string?> { [KeySetting] = key }, logs);
        await factory.WaitForSchemaAsync(TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        var health = await client.GetStringAsync("/health", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(key, health, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_rejected_key_is_logged_by_its_setting_name_and_its_value_is_not_logged()
    {
        var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(31));
        var logs = new CapturingLoggerProvider();
        using var tokens = new TestTokens();
        await using var factory = new EnvironmentsServiceFactory(tokens, new Dictionary<string, string?> { [KeySetting] = shortKey }, logs);
        await factory.WaitForSchemaAsync(TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();

        var health = await client.GetFromJsonAsync<Dictionary<string, string>>("/health", TestContext.Current.CancellationToken);

        Assert.Equal(OptionalIntegrationStatus.Unavailable, health!["secrets"]);
        Assert.Contains(logs.Lines, line => line.Contains("must decode to 32 bytes", StringComparison.Ordinal));
        Assert.Contains(logs.Lines, line => line.Contains(KeySetting, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(shortKey, StringComparison.Ordinal));
    }

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
