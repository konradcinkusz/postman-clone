using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Services;
using PostmanClone.ServiceDefaults.Validation;

namespace PostmanClone.CollectionsService.Api;

/// <summary>
/// The saved-request routes of CONTRACTS §2. A request is created inside a collection, then read, updated, duplicated,
/// moved or deleted by its id. Every answer that carries a request sends its version as the <c>ETag</c>.
/// </summary>
public static class RequestEndpoints
{
    public static RouteGroupBuilder MapRequestEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/collections/{id:guid}/requests", async (Guid id, CreateRequestBody body, HttpResponse http, ClaimsPrincipal user, IRequestService service, CancellationToken ct) =>
                Served(http, await service.CreateAsync(Caller.From(user), id, body, ct),
                    request => Results.Created($"/api/v1/requests/{request.Id}", request)))
            .ValidateDataAnnotations();

        var requests = api.MapGroup("/requests");

        requests.MapGet("{id:guid}", async (Guid id, HttpResponse http, ClaimsPrincipal user, IRequestService service, CancellationToken ct) =>
            Served(http, await service.GetAsync(Caller.From(user), id, ct), request => Results.Ok(request)));

        requests.MapPut("{id:guid}", UpdateAsync).ValidateDataAnnotations();

        requests.MapPost("{id:guid}/duplicate", async (Guid id, HttpResponse http, ClaimsPrincipal user, IRequestService service, CancellationToken ct) =>
            Served(http, await service.DuplicateAsync(Caller.From(user), id, ct),
                request => Results.Created($"/api/v1/requests/{request.Id}", request)));

        requests.MapPost("{id:guid}/move", async (Guid id, MoveRequestBody body, HttpResponse http, ClaimsPrincipal user, IRequestService service, CancellationToken ct) =>
                Served(http, await service.MoveAsync(Caller.From(user), id, body, ct), request => Results.Ok(request)))
            .ValidateDataAnnotations();

        requests.MapDelete("{id:guid}", async (Guid id, ClaimsPrincipal user, IRequestService service, CancellationToken ct) =>
            Responses.From(await service.DeleteAsync(Caller.From(user), id, ct)));

        return api;
    }

    /// <summary>PUT: the optional <c>If-Match</c> precondition is parsed here, so a malformed one is a 400 before any read.</summary>
    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateRequestBody body,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        HttpResponse http,
        ClaimsPrincipal user,
        IRequestService service,
        CancellationToken ct)
    {
        var precondition = EntityTags.ParseIfMatch(ifMatch);
        if (precondition.Failure is { } refusal)
        {
            return Responses.Problem(refusal);
        }

        return Served(http, await service.UpdateAsync(Caller.From(user), id, precondition.Value, body, ct), request => Results.Ok(request));
    }

    /// <summary>The answer for one request: the problem on a refusal, otherwise the success result with its <c>ETag</c>.</summary>
    private static IResult Served(HttpResponse http, Outcome<SavedRequestDto> outcome, Func<SavedRequestDto, IResult> onSuccess)
    {
        if (outcome.Failure is { } failure)
        {
            return Responses.Problem(failure);
        }

        var request = outcome.Value!;
        http.Headers.ETag = EntityTags.Format(request.Version);
        return onSuccess(request);
    }
}
