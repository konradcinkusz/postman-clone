using Microsoft.AspNetCore.Http;
using PostmanClone.CollectionsService.Services;
using PostmanClone.ServiceDefaults.Problems;

namespace PostmanClone.CollectionsService.Api;

/// <summary>Turns a service outcome into the HTTP answer: the success result, or an RFC 7807 problem with its code.</summary>
public static class Responses
{
    public static IResult From<T>(Outcome<T> outcome, Func<T, IResult> onSuccess) =>
        outcome.Failure is { } failure ? Problem(failure) : onSuccess(outcome.Value!);

    public static IResult From(Outcome outcome) =>
        outcome.Failure is { } failure ? Problem(failure) : Results.NoContent();

    public static IResult Problem(Failure failure)
    {
        var extensions = new Dictionary<string, object?> { [ProblemDetailsExtensions.CodeExtension] = failure.Code };
        if (failure.Errors is not null)
        {
            extensions["errors"] = failure.Errors;
        }

        return Results.Problem(title: failure.Title, statusCode: failure.Status, extensions: extensions);
    }
}
