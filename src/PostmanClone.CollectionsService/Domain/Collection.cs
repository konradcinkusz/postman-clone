namespace PostmanClone.CollectionsService.Domain;

/// <summary>A named group of folders and saved requests, owned by one workspace (CONTRACTS §2).</summary>
public sealed class Collection
{
    public Guid Id { get; set; }

    /// <summary>The workspace (authservice organization) the collection belongs to. Set from the token only (CONTRACTS §0).</summary>
    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The user who created the collection (the token's <c>sub</c>).</summary>
    public Guid CreatedBy { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Incremented on every change; the optimistic-concurrency token for <c>If-Match</c> (CONTRACTS §2).</summary>
    public int Version { get; set; }
}
