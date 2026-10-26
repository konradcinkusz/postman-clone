using Npgsql;

namespace PostmanClone.ServiceDefaults.Database;

/// <summary>
/// Accepts a PostgreSQL connection string in either form: the keyword form Npgsql reads
/// (<c>Host=...;Database=...</c>) or a URI (<c>postgres://user:password@host:5432/database</c>),
/// which Npgsql does not parse itself. Both come out in keyword form.
/// </summary>
public static class ConnectionStringNormalizer
{
    public static string Normalize(string connectionString)
    {
        var value = connectionString.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && userInfo[0].Length > 0)
        {
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        }

        if (userInfo.Length > 1)
        {
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        return builder.ConnectionString;
    }
}
