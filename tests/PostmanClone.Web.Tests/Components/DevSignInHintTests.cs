extern alias seeder;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.Hosting;
using SeederDemoData = seeder::PostmanClone.Seeder.DemoData;

namespace PostmanClone.Web.Tests.Components;

/// <summary>ADR-0030: the demo accounts are listed on the sign-in page in Development with the seed on, and nowhere else.</summary>
public sealed class DevSignInHintTests
{
    private sealed class StubAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-token", "__RequestVerificationToken");
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static BunitContext Page(string environment, string? seedEnabled)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<AntiforgeryStateProvider>(new StubAntiforgery());
        context.Services.AddSingleton<IHostEnvironment>(new Env(environment));
        var values = new Dictionary<string, string?>();
        if (seedEnabled is not null)
        {
            values["Seed:Enabled"] = seedEnabled;
        }

        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return context;
    }

    [Fact]
    public void In_Development_with_the_seed_on_the_page_lists_every_account_and_the_password()
    {
        using var context = Page("Development", "true");

        var cut = context.Render<SignIn>();

        var rows = cut.FindAll("[data-testid='dev-seed-account']");
        Assert.Equal(DevSignInHint.Accounts.Count, rows.Count);
        foreach (var account in DevSignInHint.Accounts)
        {
            Assert.Contains(account.Email, cut.Markup, StringComparison.Ordinal);
        }

        Assert.Equal(DevSignInHint.Password, cut.Find("[data-testid='dev-seed-password']").TextContent);
    }

    [Theory]
    [InlineData("Production", "true")]
    [InlineData("Staging", "true")]
    [InlineData("Production", null)]
    [InlineData("Development", null)]
    [InlineData("Development", "false")]
    public void Outside_Development_or_without_the_seed_the_hint_is_absent(string environment, string? seedEnabled)
    {
        using var context = Page(environment, seedEnabled);

        var cut = context.Render<SignIn>();

        Assert.Empty(cut.FindAll("[data-testid='dev-seed-hint']"));
        Assert.DoesNotContain(DevSignInHint.Password, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("demo.owner@", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hint_lists_exactly_the_cast_the_seeder_creates_with_its_password()
    {
        var seeded = SeederDemoData.Cast.Select(p => (p.Email, p.Role, p.Workspace)).ToArray();
        var hinted = DevSignInHint.Accounts.Select(r => (r.Email, r.Role, r.Workspace)).ToArray();

        Assert.Equal(seeded, hinted);
        Assert.Equal(SeederDemoData.DefaultPassword, DevSignInHint.Password);
    }
}
