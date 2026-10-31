using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Interop.Postman;

/// <summary>
/// What a file asks to create, before anything is written: the collection, its folders and its requests, each with the
/// sort order it will have. A folder or request names its parent by <c>Key</c>, a number the importer gave it, so nothing
/// here is a database id.
/// </summary>
public sealed record ImportPlan(
    string Name,
    string? Description,
    IReadOnlyList<PlannedFolder> Folders,
    IReadOnlyList<PlannedRequest> Requests,
    IReadOnlyList<ImportWarning> Warnings);

/// <summary>A folder to create. <see cref="ParentKey"/> is null for the top of the collection.</summary>
public sealed record PlannedFolder(int Key, int? ParentKey, string Name, int SortOrder);

/// <summary>A request to create in <see cref="FolderKey"/> (null: the top of the collection). Its definition is validated and normalized.</summary>
public sealed record PlannedRequest(int? FolderKey, string Name, string? Description, RequestDefinition Definition, int SortOrder);
