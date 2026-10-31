using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Services;

/// <summary>Entity to contract. The entities never leave the service; the contract records are what the API returns.</summary>
public static class Mapping
{
    public static CollectionDto ToDto(Collection collection) => new(
        collection.Id,
        collection.Name,
        collection.Description,
        collection.CreatedBy,
        collection.CreatedAt,
        collection.UpdatedAt);

    public static FolderDto ToDto(Folder folder) => new(
        folder.Id,
        folder.CollectionId,
        folder.ParentFolderId,
        folder.Name,
        folder.SortOrder);

    public static RequestSummary ToDto(SavedRequest request) => new(
        request.Id,
        request.CollectionId,
        request.FolderId,
        request.Name,
        request.Method,
        request.SortOrder,
        request.UpdatedAt);
}
