using Npgsql;
using Xunit;

namespace PostmanClone.TestKit;

/// <summary>
/// A throwaway PostgreSQL database per test class, created from <c>TEST_POSTGRES</c> and dropped afterwards.
/// Without <c>TEST_POSTGRES</c> the fixture is disabled and tests call <see cref="Assert.Skip"/> with the reason,
/// so the run reports them as skipped rather than passed (TESTING-STRATEGY). The variable's value is never printed.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "TEST_POSTGRES";

    private string? _adminConnectionString;
    private string? _databaseName;

    /// <summary>True when <c>TEST_POSTGRES</c> names a server to create databases on.</summary>
    public bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable));

    /// <summary>Connection string of this fixture's own database. Empty when <see cref="Enabled"/> is false.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        _adminConnectionString = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(EnvironmentVariable)!)
        {
            Database = "postgres",
        }.ConnectionString;

        _databaseName = $"pc_test_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_adminConnectionString is null || _databaseName is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
