using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>
/// The sign-in objects wired together without a host: the real store, refresher, verifier and lifecycle, the fake
/// authservice behind a real <see cref="AuthServiceClient"/>, and a fake clock. Times are relative to "now" at creation.
/// </summary>
internal sealed class AuthHarness : IDisposable
{
    public AuthHarness()
    {
        Clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Tokens = new TestTokens(Clock);
        Auth = new FakeAuthService(Tokens);

        var services = new ServiceCollection();
        services.AddHttpClient(BackendServices.AuthServiceName, client => client.BaseAddress = new Uri("http://authservice"))
            .ConfigurePrimaryHttpMessageHandler(() => Auth.Handler);
        Client = new AuthServiceClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());

        var options = Options.Create(new AuthServiceOptions
        {
            Issuer = "PostmanClone",
            Audience = "PostmanClone",
            MetadataAddress = "https://authservice.test/.well-known/openid-configuration",
            RequireHttpsMetadata = false,
        });
        Verifier = new AccessTokenVerifier(Tokens.ConfigurationManager(), options);
        Store = new ServerSessionStore(Clock);
        Refresher = new TokenRefresher(Store, Client, Verifier, Clock);
        Lifecycle = new SessionLifecycle(Store, Verifier, Client, Refresher);
        Revalidator = new SessionRevalidator(Refresher);
    }

    public FakeTimeProvider Clock { get; }

    public TestTokens Tokens { get; }

    public FakeAuthService Auth { get; }

    public AuthServiceClient Client { get; }

    public AccessTokenVerifier Verifier { get; }

    public ServerSessionStore Store { get; }

    public TokenRefresher Refresher { get; }

    public SessionLifecycle Lifecycle { get; }

    public SessionRevalidator Revalidator { get; }

    /// <summary>Signs the known user in through the fake, the way the sign-in endpoint does, and returns the session.</summary>
    public async Task<ServerSession> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        var answer = await Client.LoginAsync(FakeAuthService.KnownEmail, FakeAuthService.KnownPassword, cancellationToken);
        return (await Lifecycle.StartAsync(answer.Value!, cancellationToken))!;
    }

    public void Dispose() => Tokens.Dispose();
}
