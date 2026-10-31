using System.Text.Json;

namespace PostmanClone.CollectionsService.Domain;

/// <summary>
/// A saved request. The <see cref="Definition"/> is the whole <c>RequestDefinition</c> (CONTRACTS §1) stored as
/// <c>jsonb</c>; <see cref="Method"/> repeats its method so lists can show it without reading the document.
/// </summary>
public sealed class SavedRequest
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid CollectionId { get; set; }

    /// <summary>The folder holding the request, or null when it sits directly in the collection.</summary>
    public Guid? FolderId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The HTTP method, denormalized from <see cref="Definition"/> (GET, POST, ...).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>The <c>RequestDefinition</c> JSON document. Written and read by the request endpoints (WP13), always the validated document.</summary>
    public JsonDocument Definition { get; set; } = JsonDocument.Parse("{}");

    public int SortOrder { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; }
}
