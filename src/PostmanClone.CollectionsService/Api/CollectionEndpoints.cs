using System.Security.Claims;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Services;
using PostmanClone.ServiceDefaults.Paging;
using PostmanClone.ServiceDefaults.Validation;

namespace PostmanClone.CollectionsService.Api;

/// <summary>The collection routes of CONTRACTS §2, under <c>/api/v1/collections</c>.</summary>
public static class CollectionEndpoints
{
    public static RouteGroupBuilder MapCollectionEndpoints(this RouteGroupBuilder api)
    {
        var collections = api.MapGroup("/collections");

        collections.MapGet("", async (string? q, int? page, int? pageSize, ClaimsPrincipal user, ICollectionService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(Caller.From(user), q, PageRequest.Clamp(page, pageSize), ct)));

        collections.MapPost("", async (CreateCollectionRequest request, ClaimsPrincipal user, ICollectionService service, CancellationToken ct) =>
                Responses.From(await service.CreateAsync(Caller.From(user), request, ct),
                    collection => Results.Created($"/api/v1/collections/{collection.Id}", collection)))
            .ValidateDataAnnotations();

        collections.MapGet("{id:guid}", async (Guid id, ClaimsPrincipal user, ICollectionService service, CancellationToken ct) =>
            Responses.From(await service.GetTreeAsync(Caller.From(user), id, ct), tree => Results.Ok(tree)));

        collections.MapPut("{id:guid}", async (Guid id, UpdateCollectionRequest request, ClaimsPrincipal user, ICollectionService service, CancellationToken ct) =>
                Responses.From(await service.UpdateAsync(Caller.From(user), id, request, ct), collection => Results.Ok(collection)))
            .ValidateDataAnnotations();

        collections.MapDelete("{id:guid}", async (Guid id, ClaimsPrincipal user, ICollectionService service, CancellationToken ct) =>
            Responses.From(await service.DeleteAsync(Caller.From(user), id, ct)));

        return api;
    }
}
