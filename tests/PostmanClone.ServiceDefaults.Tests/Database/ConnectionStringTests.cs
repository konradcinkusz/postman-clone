using Microsoft.Extensions.Hosting;
using Npgsql;
using PostmanClone.ServiceDefaults.Database;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Database;

public sealed class ConnectionStringTests
{
    [Fact]
    public void A_postgres_uri_is_normalized_to_keyword_form()
    {
        // gitleaks:allow — a fixture, not a credential
        var normalized = ConnectionStringNormalizer.Normalize("postgres://probe-user:p%40ss@db.internal:6543/probe_db");

        var parts = new NpgsqlConnectionStringBuilder(normalized);
        Assert.Equal("db.internal", parts.Host);
        Assert.Equal(6543, parts.Port);
        Assert.Equal("probe_db", parts.Database);
        Assert.Equal("probe-user", parts.Username);
        Assert.Equal("p@ss", parts.Password);
    }

    [Fact]
    public void A_keyword_form_string_is_trimmed_and_otherwise_unchanged()
    {
        Assert.Equal("Host=localhost;Database=probe", ConnectionStringNormalizer.Normalize("  Host=localhost;Database=probe  "));
    }

    [Fact]
    public void An_unknown_provider_fails_at_startup_and_names_the_setting()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["DATABASE_PROVIDER"] = "SqlServer";

        var error = Assert.Throws<InvalidOperationException>(() => builder.AddDatabaseContext<ProbeContext>("probe"));

        Assert.Contains("DATABASE_PROVIDER", error.Message);
    }

    [Fact]
    public void A_postgres_host_without_its_connection_string_fails_and_names_the_connection()
    {
        var builder = Host.CreateApplicationBuilder();

        var error = Assert.Throws<InvalidOperationException>(() => builder.AddDatabaseContext<ProbeContext>("probe"));

        Assert.Contains("'probe'", error.Message);
    }
}
