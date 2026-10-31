using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>
/// The Web hosted in memory with a fake authservice behind its <c>authservice</c> client, and a test key that the Web
/// trusts for token signatures. The environment is Development over plain http, so the session cookie is the
/// unprefixed one; the Secure rules have their own unit tests.
/// </summary>
public class AuthHostFactory : WebApplicationFactory<Program>
{
    private readonly TestTokens _tokens = new();

    public AuthHostFactory()
    {
        AuthService = new FakeAuthService(_tokens);
    }

    public FakeAuthService AuthService { get; }

    /// <summary>The fake CollectionsService behind the Web's <c>collections</c> client, for the import and export endpoints.</summary>
    public PostmanClone.Web.Tests.Collections.FakeCollectionsService Collections { get; } = new(Guid.NewGuid());

    /// <summary>Every line the hosted Web logs, for the tests that check what must not be logged.</summary>
    public PostmanClone.Web.Tests.Collections.CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient(BackendServices.AuthServiceName).ConfigurePrimaryHttpMessageHandler(() => AuthService.Handler);
            services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(_tokens.ConfigurationManager());
            services.AddHttpClient(BackendServices.CollectionsName).ConfigurePrimaryHttpMessageHandler(() => Collections.Handler);
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(Logs);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tokens.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>The sign-in forms as a browser would post them: an antiforgery pair fetched from a page, then the fields.</summary>
public static class SignInForms
{
    public const string CookieHeaderName = "Cookie";

    private static readonly Regex TokenField = new("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");

    /// <summary>
    /// The antiforgery token and the cookie that goes with it, from a page as the browser would load it. The token is
    /// bound to the user it was minted for, so a form that a signed-in user posts takes its token from a signed-in page.
    /// </summary>
    public static async Task<(string Cookie, string Token)> AntiforgeryAsync(HttpClient client, string page, string? sessionCookie, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(Get(page, sessionCookie), cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = TokenField.Match(html).Groups[1].Value;
        var antiforgery = response.Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
        return (antiforgery.Split(';')[0], token);
    }

    /// <summary>A POST with the cookie and the form fields. The token is sent as the form's hidden field.</summary>
    public static HttpRequestMessage Post(string path, IReadOnlyDictionary<string, string> fields, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };
        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation(CookieHeaderName, cookie);
        }

        return request;
    }

    /// <summary>A GET with the cookie, as a browser sends a navigation.</summary>
    public static HttpRequestMessage Get(string path, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation(CookieHeaderName, cookie);
        }

        return request;
    }

    /// <summary>The name and value of a cookie the response set, or null when it set none with that name.</summary>
    public static string? SetCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(cookie => cookie.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    /// <summary>The value a session cookie holds, from a Set-Cookie header.</summary>
    public static string ValueOf(string setCookie) => setCookie.Split(';')[0].Split('=', 2)[1];
}
