using System.Net;
using System.Text;
using PostmanClone.Agent.Config;
using PostmanClone.Agent.Remote;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.Agent.Tests;

/// <summary>The credential file and the server client (ADR-0026 Decision 11, CONTRACTS A2.3 and A2.4).</summary>
public sealed class ConfigAndClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pc-agent-config-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void The_config_round_trips_every_field()
    {
        var store = new AgentConfigStore(_directory);
        var id = Guid.NewGuid();
        store.Save(new AgentConfig
        {
            ServerBaseUrl = "https://estate.example",
            AllowedWebOrigins = ["https://app.example"],
            AgentId = id,
            AgentToken = "token-value",
            PairingTokenHash = "hash-value",
            AllowPrivate = false,
        });

        var loaded = store.Load();

        Assert.Equal("https://estate.example", loaded.ServerBaseUrl);
        Assert.Equal(["https://app.example"], loaded.AllowedWebOrigins);
        Assert.Equal(id, loaded.AgentId);
        Assert.True(loaded.IsPaired);
        Assert.False(loaded.AllowPrivate);
    }

    [Fact]
    public void A_missing_file_loads_as_an_unpaired_default_with_private_targets_allowed()
    {
        var loaded = new AgentConfigStore(_directory).Load();

        Assert.False(loaded.IsPaired);
        Assert.True(loaded.AllowPrivate);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void The_file_is_readable_by_the_user_alone()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "Unix file modes are checked on Linux CI; Windows uses the profile ACL (OPEN-ITEMS).");
        var store = new AgentConfigStore(_directory);

        store.Save(new AgentConfig { AgentToken = "token-value" });

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(store.FilePath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(_directory));
    }

    [Fact]
    public void Forget_deletes_the_credentials_file()
    {
        var store = new AgentConfigStore(_directory);
        store.Save(new AgentConfig { AgentToken = "token-value" });

        store.Delete();

        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void A_pairing_token_matches_only_its_own_hash()
    {
        var token = PairingTokens.New();

        Assert.True(PairingTokens.Matches(token, PairingTokens.Hash(token)));
        Assert.False(PairingTokens.Matches(PairingTokens.New(), PairingTokens.Hash(token)));
        Assert.False(PairingTokens.Matches(null, PairingTokens.Hash(token)));
        Assert.False(PairingTokens.Matches(token, null));
    }

    [Fact]
    public async Task The_server_client_sends_the_bearer_only_where_it_is_given()
    {
        var seen = new List<string?>();
        using var client = new AgentServerClient(new FakeHttpMessageHandler(request =>
        {
            seen.Add(request.Headers.Authorization?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"agentId\":\"" + Guid.NewGuid() + "\",\"workspaceId\":\"" + Guid.NewGuid() + "\",\"expiresAt\":\"2030-01-01T00:00:00Z\"}", Encoding.UTF8, "application/json"),
            };
        }));

        await client.MeAsync("https://estate.example", "agent-token-value", CancellationToken.None);

        Assert.Equal(["Bearer agent-token-value"], seen);
    }

    [Fact]
    public async Task A_redirect_from_the_server_is_a_failure_and_the_token_is_not_followed_to_it()
    {
        // Threat: a server answer that sends the agent token to another address.
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://elsewhere.example/steal");
            return response;
        });
        using var client = new AgentServerClient(handler);

        var reply = await client.ResolveAsync("https://estate.example", "agent-token-value", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, reply.Status);
        Assert.Equal("upstream_unavailable", reply.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_problem_answer_keeps_its_contract_code()
    {
        using var client = new AgentServerClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"status\":400,\"code\":\"pairing_code_invalid\"}", Encoding.UTF8, "application/problem+json"),
        }));

        var reply = await client.PairAsync("https://estate.example", "ABCDEFGHJK", "agent", "linux-x64", "1.0.0", CancellationToken.None);

        Assert.Equal(400, reply.Status);
        Assert.Equal("pairing_code_invalid", reply.Code);
        Assert.False(reply.Ok);
    }
}
