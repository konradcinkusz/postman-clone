using System.ComponentModel.DataAnnotations;

namespace PostmanClone.CollectionsService.Contracts;

/// <summary>A list page (CONTRACTS §0): the items of the page, the page asked for and the total of the query.</summary>
public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>Name rules shared by every name in the collections API (CONTRACTS §2: 1..100 or 1..200 characters, never blank).</summary>
public static class NameRules
{
    /// <summary>A name must contain at least one non-space character; a whitespace-only name is refused.</summary>
    public const string NonBlank = @"\S";

    public const string NonBlankMessage = "The name cannot be blank.";
}

public sealed record CreateCollectionRequest(
    [property: Required, StringLength(100, MinimumLength = 1), RegularExpression(NameRules.NonBlank, ErrorMessage = NameRules.NonBlankMessage)] string Name,
    [property: StringLength(500)] string? Description);

/// <summary>A full replacement of the collection's name and description (CONTRACTS §2: PUT).</summary>
public sealed record UpdateCollectionRequest(
    [property: Required, StringLength(100, MinimumLength = 1), RegularExpression(NameRules.NonBlank, ErrorMessage = NameRules.NonBlankMessage)] string Name,
    [property: StringLength(500)] string? Description);

public sealed record CreateFolderRequest(
    [property: Required, StringLength(100, MinimumLength = 1), RegularExpression(NameRules.NonBlank, ErrorMessage = NameRules.NonBlankMessage)] string Name,
    Guid? ParentFolderId);

public sealed record RenameFolderRequest(
    [property: Required, StringLength(100, MinimumLength = 1), RegularExpression(NameRules.NonBlank, ErrorMessage = NameRules.NonBlankMessage)] string Name);

/// <summary>Moves a folder under <see cref="ParentFolderId"/> (null: the top of the collection) at <see cref="SortOrder"/>.</summary>
public sealed record MoveFolderRequest(Guid? ParentFolderId, [property: Range(0, int.MaxValue)] int? SortOrder);

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

/// <summary>A saved request in a tree listing. The request endpoints that write requests arrive with WP13.</summary>
public sealed record RequestSummary(
    Guid Id,
    Guid CollectionId,
    Guid? FolderId,
    string Name,
    string Method,
    int SortOrder,
    DateTimeOffset UpdatedAt);

/// <summary>The whole collection as a flat list; the client builds the tree from <see cref="FolderDto.ParentFolderId"/>.</summary>
public sealed record CollectionTree(CollectionDto Collection, IReadOnlyList<FolderDto> Folders, IReadOnlyList<RequestSummary> Requests);
