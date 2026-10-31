namespace PostmanClone.Web.Api;

// Copies of CollectionsService's contract types (CONTRACTS §1 and §2, src/PostmanClone.CollectionsService/Contracts).
// The Web cannot reference the service, so the shapes are written by hand here. CollectionsRoundTripTests serializes the
// service's real types and reads them back through these copies, so a renamed or retyped field is a failing test.

/// <summary>A list page (CONTRACTS §0): the items, the page asked for, the page size and the total of the query.</summary>
public sealed record PageResult<T>(List<T> Items, int Page, int PageSize, int Total);

public sealed record KeyValue(string Key, string Value, bool Enabled = true, string? Description = null);

public sealed record RequestBody(string Type, string? RawLanguage, string? Raw, List<KeyValue>? Fields);

public sealed record RequestAuth(
    string Type,
    string? BearerToken,
    string? Username,
    string? Password,
    string? ApiKeyName,
    string? ApiKeyValue,
    string? ApiKeyIn);

/// <summary>The whole request as stored (CONTRACTS §1). The shell reads only its method; the builder (WP19) reads the rest.</summary>
public sealed record RequestDefinition(
    string Method,
    string Url,
    List<KeyValue>? Params,
    List<KeyValue>? Headers,
    RequestBody? Body,
    RequestAuth? Auth);

public sealed record CollectionSummary(
    Guid Id,
    string Name,
    string? Description,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int RequestCount);

public sealed record CollectionDto(
    Guid Id,
    string Name,
    string? Description,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record FolderDto(Guid Id, Guid CollectionId, Guid? ParentFolderId, string Name, int SortOrder);

/// <summary>A saved request in a tree listing. The full document is <see cref="SavedRequestDto"/>.</summary>
public sealed record RequestSummary(
    Guid Id,
    Guid CollectionId,
    Guid? FolderId,
    string Name,
    string Method,
    int SortOrder,
    DateTimeOffset UpdatedAt);

/// <summary>A saved request with its definition. <see cref="Version"/> is the value an <c>If-Match</c> compares (CONTRACTS §2).</summary>
public sealed record SavedRequestDto(
    Guid Id,
    Guid CollectionId,
    Guid? FolderId,
    string Name,
    string? Description,
    RequestDefinition Request,
    int SortOrder,
    int Version,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>The whole collection as a flat list; the Web builds the tree from <see cref="FolderDto.ParentFolderId"/>.</summary>
public sealed record CollectionTree(CollectionDto Collection, List<FolderDto> Folders, List<RequestSummary> Requests);

// Request bodies the Web sends. Names follow the service's, with a Body suffix so the two sets of types never collide.

public sealed record CreateCollectionBody(string Name, string? Description);

public sealed record UpdateCollectionBody(string Name, string? Description);

public sealed record CreateFolderBody(string Name, Guid? ParentFolderId);

public sealed record RenameFolderBody(string Name);

public sealed record UpdateSavedRequestBody(string Name, string? Description, RequestDefinition Request);

/// <summary>Creates a saved request in a collection (CONTRACTS §2). A null <see cref="FolderId"/> puts it at the top of the collection.</summary>
public sealed record CreateSavedRequestBody(string Name, Guid? FolderId, string? Description, RequestDefinition Request);

/// <summary>Moves a saved request. Null <see cref="CollectionId"/> keeps it in its collection; null <see cref="FolderId"/> puts it at the top.</summary>
public sealed record MoveRequestBody(Guid? CollectionId, Guid? FolderId, int? SortOrder);
