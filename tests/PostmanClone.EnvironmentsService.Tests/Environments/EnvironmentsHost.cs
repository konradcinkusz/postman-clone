using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PostmanClone.EnvironmentsService.Secrets;
using PostmanClone.TestKit;

namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>A signed-in user acting in one workspace with one role (CONTRACTS §0).</summary>
public sealed record Caller(Guid Workspace, string Role, string UserId)
{
    public static Caller Owner(Guid workspace, string userId = "owner-user") => new(workspace, "Owner", userId);

    public static Caller Admin(Guid workspace, string userId = "admin-user") => new(workspace, "Admin", userId);

    public static Caller Member(Guid workspace, string userId = "member-user") => new(workspace, "Member", userId);
}

/// <summary>
/// The environment service running in-process: its own token issuer, a fresh database, and a key unless the test
/// asks for none. Each host is independent of the others, so two workspaces can share an id without sharing rows.
/// </summary>
public sealed class EnvironmentsHost : IAsyncDisposable
{
    public const string EnvironmentsRoute = "/api/v1/environments";
    public const string KeySetting = SecretProtectorFactory.KeySetting;

    private EnvironmentsHost(TestTokens tokens, EnvironmentsServiceFactory factory)
    {
        Tokens = tokens;
        Factory = factory;
        Client = factory.CreateClient();
    }

    public TestTokens Tokens { get; }

    public EnvironmentsServiceFactory Factory { get; }

    public HttpClient Client { get; }

    /// <summary>Starts the service with a valid key, unless <paramref name="settings"/> says otherwise, and waits for the schema.</summary>
    public static async Task<EnvironmentsHost> StartAsync(
        IReadOnlyDictionary<string, string?>? settings = null,
        ILoggerProvider? logs = null,
        TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        var tokens = new TestTokens();
        var factory = new EnvironmentsServiceFactory(tokens, settings ?? KeyedSettings(), logs, clock);
        await factory.WaitForSchemaAsync(cancellationToken);
        return new EnvironmentsHost(tokens, factory);
    }

    /// <summary>Settings with a valid, random 32-byte key. The key exists only in memory for the test.</summary>
    public static Dictionary<string, string?> KeyedSettings() => new()
    {
        [KeySetting] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    };

    /// <summary>Settings with no key: secret writes answer 503, and the rest of the service works (P8).</summary>
    public static Dictionary<string, string?> KeylessSettings() => [];

    public string TokenFor(Caller caller) =>
        Tokens.Issue(TestTokens.Membership(caller.Workspace, caller.Role).Append(new Claim("sub", caller.UserId)));

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, Caller caller, object? body = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(caller)) },
        };
        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        }

        return Client.SendAsync(request, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        Tokens.Dispose();
    }
}
