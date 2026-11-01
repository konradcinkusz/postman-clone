using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PostmanClone.ServiceDefaults.RateLimiting;

/// <summary>The policy names endpoints opt into with <c>RequireRateLimiting</c> (CONTRACTS §0).</summary>
public static class RateLimitPolicies
{
    public const string Api = "api";
    public const string Execute = "execute";
    public const string Auth = "auth";
}

/// <summary>
/// The kernel's rate limits (CONTRACTS §0, SERVICE-API-PATTERNS §1). <c>api</c> (300 a minute) and <c>execute</c>
/// (60 a minute) count per signed-in user and fall back to the client address. <c>auth</c> (10 a minute) counts per
/// client address, because the endpoints it protects have no user yet. A rejection is an immediate 429 with one
/// body shape: a rejected request is never queued.
/// </summary>
public static class RateLimitingExtensions
{
    public static IServiceCollection AddRateLimitingDefaults(this IServiceCollection services, IConfiguration configuration)
    {
        var trustForwardedFor = configuration.GetValue("Proxy:TrustForwardedFor", false);
        // The sign-in forms' limit per client address. 10 is the default; a test stack that signs in many users from one
        // address raises it (RateLimiting:AuthPermitsPerMinute). It is never below 1.
        var authPermitsPerMinute = Math.Max(1, configuration.GetValue("RateLimiting:AuthPermitsPerMinute", 10));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;
            FixedWindowPolicy(options, RateLimitPolicies.Api, permitsPerMinute: 300,
                context => ClientIdentity.ForUser(context, trustForwardedFor));
            FixedWindowPolicy(options, RateLimitPolicies.Execute, permitsPerMinute: 60,
                context => ClientIdentity.ForUser(context, trustForwardedFor));
            FixedWindowPolicy(options, RateLimitPolicies.Auth, permitsPerMinute: authPermitsPerMinute,
                context => ClientIdentity.ForAddress(context, trustForwardedFor));
        });
        return services;
    }

    private static void FixedWindowPolicy(RateLimiterOptions options, string policy, int permitsPerMinute, Func<HttpContext, string> partitionKey) =>
        options.AddPolicy<string>(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    private static ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers["Retry-After"] = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return new ValueTask(response.WriteAsJsonAsync(
            new { code = "rate_limited", title = "Too many requests. Try again later.", status = 429 },
            cancellationToken));
    }
}
