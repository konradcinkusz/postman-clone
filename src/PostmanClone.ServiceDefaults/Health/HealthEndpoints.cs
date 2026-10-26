using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PostmanClone.ServiceDefaults.Integrations;

namespace PostmanClone.ServiceDefaults.Health;

/// <summary>
/// The health payload (CONTRACTS §0, P8): <c>status</c>, then one flat key per optional integration
/// (<c>"secrets": "unavailable"</c>). Optional integrations never change the HTTP status; only required
/// checks (database reachable, migrations applied) answer 503.
/// </summary>
public static class HealthEndpoints
{
    public static readonly HealthCheckOptions ReadinessOptions = new() { ResponseWriter = WriteAsync };

    public static readonly HealthCheckOptions LivenessOptions = new()
    {
        Predicate = check => check.Tags.Contains(LiveTag),
        ResponseWriter = WriteAsync,
    };

    /// <summary>Only checks tagged <c>live</c> decide <c>/alive</c>.</summary>
    public const string LiveTag = "live";

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var payload = new Dictionary<string, string>
        {
            ["status"] = report.Status.ToString().ToLowerInvariant(),
        };

        if (report.Entries.TryGetValue(OptionalIntegrationHealthCheck.CheckName, out var integrations))
        {
            foreach (var (name, status) in integrations.Data)
            {
                payload[name] = status.ToString() ?? OptionalIntegrationStatus.Unavailable;
            }
        }

        context.Response.StatusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
