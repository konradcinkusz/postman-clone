using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>The register page states authservice's password rule before the form is posted (WP28, the register UX item).</summary>
public sealed class RegisterPageTests : IClassFixture<AuthHostFactory>
{
    private readonly AuthHostFactory _factory;

    public RegisterPageTests(AuthHostFactory factory) => _factory = factory;

    [Fact]
    public async Task The_register_page_shows_the_full_password_rule_and_links_the_input_to_it()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        using var response = await client.GetAsync("/register", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"register-password-rule\"", html, StringComparison.Ordinal);
        Assert.Contains("at least 8 characters, with an uppercase letter, a lowercase letter, a digit and a symbol", html, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"register-password-rule\"", html, StringComparison.Ordinal);
    }
}
