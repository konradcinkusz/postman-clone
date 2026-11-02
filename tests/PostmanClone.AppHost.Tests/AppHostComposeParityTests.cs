using Aspire.Hosting.ApplicationModel;
using PostmanClone.TestKit;

namespace PostmanClone.AppHost.Tests;

/// <summary>
/// The container estate is written twice: in the AppHost (Aspire, the developer's inner loop) and in compose.yaml (CI and anyone without the
/// Aspire tooling). These tests pin that the two cannot drift apart where it matters: the same authservice image, the same variable names,
/// the same signing-key handling, the same seed wiring (ADR-0031). The AppHost is built in memory, never started.
/// </summary>
public sealed class AppHostComposeParityTests
{
    private static readonly ComposeFile Compose = ComposeFile.Load();

    // Set by compose only: the probe path for the container health check (the AppHost uses WithHttpHealthCheck instead).
    private static readonly HashSet<string> ComposeOnly = ["HEALTH_PATH"];

    [Fact]
    public async Task Authservice_uses_the_same_pinned_image_in_the_AppHost_and_in_compose()
    {
        var model = await AppHostModel.BuildAsync();
        var apphost = AppHostModel.ImageReference(model.Get("authservice"));

        Assert.Equal("ghcr.io/konradcinkusz/authservice:v0.3.4", apphost);
        Assert.Equal(apphost, Compose.ImageDefault("authservice"));
    }

    [Fact]
    public async Task Authservice_receives_the_same_variable_names_with_the_same_fixed_values()
    {
        var model = await AppHostModel.BuildAsync();
        var apphost = await model.EnvironmentAsync("authservice");
        var compose = Compose.Environment("authservice").Where(p => !ComposeOnly.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);

        // Aspire adds nothing to a container's variables on its own; every name the AppHost sets is in compose, and the other way round.
        Assert.Equal(apphost.Keys.Order(StringComparer.Ordinal), compose.Keys.Order(StringComparer.Ordinal));
        foreach (var fixedName in new[] { "Jwt__Issuer", "Jwt__Audience", "Database__SchemaMode", "Database__MigrationsAssembly", "ASPNETCORE_ENVIRONMENT" })
        {
            Assert.Equal(apphost[fixedName], compose[fixedName]);
        }
    }

    [Fact]
    public async Task The_signing_key_is_a_required_reference_in_both_and_never_a_literal()
    {
        var model = await AppHostModel.BuildAsync();
        var apphost = (await model.EnvironmentAsync("authservice"))["Jwt__PrivateKeyPem"];
        var compose = Compose.Environment("authservice")["Jwt__PrivateKeyPem"];

        // AppHost: a reference to the secret parameter. compose: a required variable, which compose-up.sh fills from the gitignored secrets.
        Assert.Contains("authservice-jwt-private-key-pem", apphost, StringComparison.Ordinal);
        Assert.DoesNotContain(AppHostModel.ConfiguredKey, apphost, StringComparison.Ordinal);
        Assert.StartsWith("${JWT_PRIVATE_KEY_PEM:?", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", compose, StringComparison.Ordinal);

        var parameter = Assert.IsType<ParameterResource>(model.Get("authservice-jwt-private-key-pem"));
        Assert.True(parameter.Secret);
    }

    [Fact]
    public void Every_signing_key_source_makes_a_PKCS8_RSA_key()
    {
        // The AppHost's generator.
        var pem = PostmanClone.AppHost.Resources.DevSecrets.GenerateSigningKeyPem();
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pem, StringComparison.Ordinal);

        // compose-up.sh and run-local-stack.sh share scripts/gen-dev-secrets.sh: genpkey writes PKCS#8, genrsa would write PKCS#1.
        var script = File.ReadAllText(Path.Combine(RepoRoot.Find(), "scripts", "gen-dev-secrets.sh"));
        Assert.Contains("openssl genpkey -algorithm RSA", script, StringComparison.Ordinal);
        Assert.DoesNotContain("genrsa", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_log_folder_the_seeder_reads_and_the_data_protection_keys_are_mounted_at_the_same_container_paths()
    {
        var model = await AppHostModel.BuildAsync();
        var mounts = model.Get("authservice").Annotations.OfType<ContainerMountAnnotation>().Select(m => m.Target).ToList();
        var compose = Compose.Volumes("authservice").Select(v => v.Split(':')[1]).ToList();

        Assert.Contains("/logs", mounts);
        Assert.Contains("/root/.aspnet/DataProtection-Keys", mounts);
        Assert.Equal(mounts.Order(StringComparer.Ordinal), compose.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_log_capturing_entrypoint_is_the_same_command()
    {
        var model = await AppHostModel.BuildAsync();
        var resource = (ContainerResource)model.Get("authservice");
        var args = await model.ArgumentsAsync("authservice");
        var compose = Compose.Entrypoint("authservice");

        Assert.Equal(compose[0], resource.Entrypoint);
        Assert.Equal(compose.Skip(1), args);
    }

    [Theory]
    [InlineData("collections-service")]
    [InlineData("environments-service")]
    [InlineData("execution-service")]
    public async Task A_data_service_trusts_the_same_issuer_and_audience_in_both(string service)
    {
        var model = await AppHostModel.BuildAsync();
        var apphost = await model.EnvironmentAsync(service);
        var compose = Compose.Environment(service);

        foreach (var name in new[] { "Auth__MetadataAddress", "Auth__Issuer", "Auth__Audience", "Auth__RequireHttpsMetadata" })
        {
            Assert.True(apphost.ContainsKey(name), $"The AppHost does not set {name} on {service}.");
            Assert.True(compose.ContainsKey(name), $"compose.yaml does not set {name} on {service}.");
        }

        Assert.Equal(apphost["Auth__Issuer"], compose["Auth__Issuer"]);
        Assert.Equal(apphost["Auth__Audience"], compose["Auth__Audience"]);
        Assert.Equal(apphost["Auth__RequireHttpsMetadata"], compose["Auth__RequireHttpsMetadata"]);
    }

    [Fact]
    public async Task The_environments_encryption_key_is_a_required_secret_reference_in_both()
    {
        var model = await AppHostModel.BuildAsync();
        var apphost = (await model.EnvironmentAsync("environments-service"))["Secrets__EncryptionKey"];
        var compose = Compose.Environment("environments-service")["Secrets__EncryptionKey"];

        Assert.Contains("environments-encryption-key", apphost, StringComparison.Ordinal);
        Assert.StartsWith("${SECRETS_ENCRYPTION_KEY:?", compose, StringComparison.Ordinal);
        Assert.True(Assert.IsType<ParameterResource>(model.Get("environments-encryption-key")).Secret);
    }

    [Fact]
    public async Task The_seed_gets_the_same_Seed_variables_in_both_and_waits_for_the_services()
    {
        var model = await AppHostModel.BuildAsync();
        var apphost = (await model.EnvironmentAsync("seed")).Keys.Where(k => k.StartsWith("Seed__", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
        var compose = Compose.Environment("seed").Keys.Where(k => k.StartsWith("Seed__", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

        Assert.Equal(apphost, compose);
        Assert.Equal("Development", (await model.EnvironmentAsync("seed"))["DOTNET_ENVIRONMENT"]);
        Assert.Equal("Development", Compose.Environment("seed")["DOTNET_ENVIRONMENT"]);

        var waits = model.Get("seed").Annotations.OfType<WaitAnnotation>().Select(w => w.Resource.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["authservice", "collections-service", "environments-service"], waits);
    }

    [Fact]
    public async Task Seeding_can_be_switched_off_and_the_seed_resource_is_then_absent()
    {
        var off = await AppHostModel.BuildAsync("Seed:Enabled=false");
        Assert.Null(off.Find("seed"));

        var defaultOn = await AppHostModel.BuildAsync();
        Assert.NotNull(defaultOn.Find("seed"));

        // Outside Development the AppHost does not add the seed, whatever the switch says.
        var production = await AppHostModel.BuildAsync("--environment=Production", "Seed:Enabled=true");
        Assert.Null(production.Find("seed"));
    }

    [Fact]
    public void Compose_runs_every_service_the_AppHost_runs()
    {
        var names = Compose.ServiceNames.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            ["authservice", "collections-service", "environments-service", "execution-service", "postgres", "seed", "web"],
            names);
    }
}
