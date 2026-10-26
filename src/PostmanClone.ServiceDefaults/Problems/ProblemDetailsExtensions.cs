using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace PostmanClone.ServiceDefaults.Problems;

/// <summary>
/// RFC 7807 problem responses with the CONTRACTS §0 <c>code</c> extension. A code the endpoint sets itself
/// (for example <c>secret_literal_not_allowed</c>) is kept; otherwise the code follows from the status.
/// </summary>
public static class ProblemDetailsExtensions
{
    private static readonly IReadOnlyDictionary<int, string> CodesByStatus = new Dictionary<int, string>
    {
        [StatusCodes.Status400BadRequest] = "validation_failed",
        [StatusCodes.Status403Forbidden] = "forbidden",
        [StatusCodes.Status404NotFound] = "not_found",
        [StatusCodes.Status409Conflict] = "conflict",
        [StatusCodes.Status413PayloadTooLarge] = "payload_too_large",
        [StatusCodes.Status429TooManyRequests] = "rate_limited",
        [StatusCodes.Status502BadGateway] = "upstream_unavailable",
    };

    public static IServiceCollection AddProblemDetailsDefaults(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            if (problem.Extensions.ContainsKey(CodeExtension))
            {
                return;
            }

            if (problem.Status is int status && CodesByStatus.TryGetValue(status, out var code))
            {
                problem.Extensions[CodeExtension] = code;
            }
        });
        return services;
    }

    /// <summary>
    /// Turns unhandled exceptions and bare status responses (for example an unmatched route) into problem
    /// responses. Call it before the endpoints in a service's <c>Program.cs</c>.
    /// </summary>
    public static WebApplication UseProblemDetailsDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    public const string CodeExtension = "code";
}
