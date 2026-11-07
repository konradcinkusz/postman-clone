using System.Net;

namespace PostmanClone.Seeder;

/// <summary>
/// What the seeder reads from its environment (the keys are the .NET configuration names with a double underscore, so the
/// AppHost, compose and the shell scripts set them the same way). Nothing here is a credential.
/// </summary>
internal sealed record SeedOptions(
    bool Enabled,
    string Environment,
    Uri AuthBaseUrl,
    Uri CollectionsBaseUrl,
    Uri EnvironmentsBaseUrl,
    string? AuthLogPath,
    string DemoPassword,
    TimeSpan WaitFor)
{
    /// <summary>Reads the options from <paramref name="get"/> (the process environment in production, a dictionary in tests).</summary>
    public static SeedOptions Read(Func<string, string?> get)
    {
        string Text(string key, string fallback) => string.IsNullOrWhiteSpace(get(key)) ? fallback : get(key)!.Trim();
        var seconds = int.TryParse(get("Seed__WaitSeconds"), out var s) && s > 0 ? s : 240;
        var environment = get("DOTNET_ENVIRONMENT") ?? get("ASPNETCORE_ENVIRONMENT") ?? "Production";
        return new SeedOptions(
            Enabled: string.Equals(get("Seed__Enabled"), "true", StringComparison.OrdinalIgnoreCase),
            Environment: environment,
            AuthBaseUrl: new Uri(Text("Seed__AuthBaseUrl", "http://localhost:5300")),
            CollectionsBaseUrl: new Uri(Text("Seed__CollectionsBaseUrl", "http://localhost:5301")),
            EnvironmentsBaseUrl: new Uri(Text("Seed__EnvironmentsBaseUrl", "http://localhost:5302")),
            AuthLogPath: string.IsNullOrWhiteSpace(get("Seed__AuthLogPath")) ? null : get("Seed__AuthLogPath")!.Trim(),
            DemoPassword: Text("Seed__DemoPassword", DemoData.DefaultPassword),
            WaitFor: TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// The refusal reason, or null when seeding may run. Three independent conditions, all required: the explicit switch, the
    /// Development environment, and targets that are local (a loopback or private address, or a single-label container name).
    /// </summary>
    public string? Refusal()
    {
        if (!Enabled)
        {
            return "Seed__Enabled is not 'true'. The seeder runs only when the switch is set explicitly.";
        }

        if (!string.Equals(Environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return $"The environment is '{Environment}', not Development. The seeder creates demo accounts with a published password and never runs outside Development.";
        }

        foreach (var (name, uri) in new[] { ("Seed__AuthBaseUrl", AuthBaseUrl), ("Seed__CollectionsBaseUrl", CollectionsBaseUrl), ("Seed__EnvironmentsBaseUrl", EnvironmentsBaseUrl) })
        {
            if (!IsLocalHost(uri.Host))
            {
                return $"{name} points at '{uri.Host}', which is not a local address. The seeder writes only to a local or container-network estate.";
            }
        }

        return null;
    }

    internal static bool IsLocalHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "host.docker.internal")
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            var b = address.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168));
        }

        // A compose or Kubernetes service name has no dot ("authservice"). A public name always has one.
        return !host.Contains('.', StringComparison.Ordinal);
    }
}
