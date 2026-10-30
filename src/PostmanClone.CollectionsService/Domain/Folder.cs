namespace PostmanClone.CollectionsService.Domain;

/// <summary>A folder inside a collection. Folders nest up to the depth limit of CONTRACTS §2.</summary>
public sealed class Folder
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid CollectionId { get; set; }

    /// <summary>The enclosing folder, or null when the folder sits at the top of its collection.</summary>
    public Guid? ParentFolderId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; }
}
