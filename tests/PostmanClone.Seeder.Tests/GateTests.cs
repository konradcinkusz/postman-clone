namespace PostmanClone.Seeder.Tests;

public sealed class GateTests
{
    private static SeedOptions Options(Dictionary<string, string?> values) => SeedOptions.Read(key => values.GetValueOrDefault(key));

    private static Dictionary<string, string?> Allowed() => new() { ["Seed__Enabled"] = "true", ["DOTNET_ENVIRONMENT"] = "Development" };

    [Fact]
    public void Development_with_the_explicit_switch_and_local_targets_is_allowed()
    {
        Assert.Null(Options(Allowed()).Refusal());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("1")]
    public void Without_the_exact_switch_the_seeder_refuses(string? value)
    {
        var values = Allowed();
        values["Seed__Enabled"] = value;

        Assert.Contains("Seed__Enabled", Options(values).Refusal());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData(null)]
    public void Outside_Development_the_seeder_refuses_even_with_the_switch(string? environment)
    {
        var values = Allowed();
        values["DOTNET_ENVIRONMENT"] = environment;

        Assert.Contains("not Development", Options(values).Refusal());
    }

    [Theory]
    [InlineData("https://auth.example.com")]
    [InlineData("http://203.0.113.9:5300")]
    public void A_public_target_is_refused(string url)
    {
        var values = Allowed();
        values["Seed__AuthBaseUrl"] = url;

        Assert.Contains("not a local address", Options(values).Refusal());
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.5", true)]
    [InlineData("192.168.1.9", true)]
    [InlineData("authservice", true)]
    [InlineData("collections-service", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("api.example.com", false)]
    public void Only_local_hosts_pass(string host, bool local)
    {
        Assert.Equal(local, SeedOptions.IsLocalHost(host));
    }

    [Fact]
    public void Options_default_to_the_local_stack_ports_and_the_published_demo_password()
    {
        var options = Options(Allowed());

        Assert.Equal("http://localhost:5300/", options.AuthBaseUrl.ToString());
        Assert.Equal("http://localhost:5301/", options.CollectionsBaseUrl.ToString());
        Assert.Equal("http://localhost:5302/", options.EnvironmentsBaseUrl.ToString());
        Assert.Equal(DemoData.DefaultPassword, options.DemoPassword);
    }

    [Fact]
    public void The_demo_password_satisfies_authservices_password_rule()
    {
        var p = DemoData.DefaultPassword;

        Assert.True(p.Length >= 8 && p.Any(char.IsDigit) && p.Any(char.IsLower) && p.Any(char.IsUpper) && p.Any(c => !char.IsLetterOrDigit(c)));
    }

    [Fact]
    public void Every_name_the_seed_creates_that_reset_may_remove_is_in_the_reserved_namespace()
    {
        Assert.True(DemoData.IsDemoName(DemoData.CollectionName));
        Assert.True(DemoData.IsDemoName(DemoData.EnvironmentName));
        Assert.False(DemoData.IsDemoName("Getting started"));
        Assert.False(DemoData.IsDemoName("my [demo] collection"));
        Assert.False(DemoData.IsDemoName(null));
    }
}
