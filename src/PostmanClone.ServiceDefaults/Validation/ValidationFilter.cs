using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace PostmanClone.ServiceDefaults.Validation;

/// <summary>
/// Runs DataAnnotations on the endpoint's model arguments before the handler (SERVICE-API-PATTERNS §3) and answers
/// <c>400 validation_failed</c> with the failures grouped by member. Only the top-level model is checked; nested
/// collections carry their own limits in the handler.
/// </summary>
public sealed class ValidationFilter : IEndpointFilter
{
    private const string RequestMember = "request";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var failures = new Dictionary<string, List<string>>();
        foreach (var argument in context.Arguments)
        {
            if (argument is null or HttpContext or CancellationToken)
            {
                continue;
            }

            var results = new List<ValidationResult>();
            if (Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
            {
                continue;
            }

            foreach (var result in results)
            {
                foreach (var member in result.MemberNames.DefaultIfEmpty(RequestMember))
                {
                    // Keys follow the JSON property names the client sent (camelCase), as CONTRACTS §0 errors do.
                    var key = JsonNamingPolicy.CamelCase.ConvertName(member);
                    if (!failures.TryGetValue(key, out var messages))
                    {
                        failures[key] = messages = [];
                    }

                    messages.Add(result.ErrorMessage ?? "Invalid value.");
                }
            }
        }

        return failures.Count == 0
            ? await next(context)
            : Results.ValidationProblem(failures.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()));
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>Validates the endpoint's request model with DataAnnotations before the handler runs.</summary>
    public static RouteHandlerBuilder ValidateDataAnnotations(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter>();

    /// <summary>Validates the request model of every endpoint in a group.</summary>
    public static RouteGroupBuilder ValidateDataAnnotations(this RouteGroupBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter>();
}
