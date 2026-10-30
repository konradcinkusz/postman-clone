namespace PostmanClone.EnvironmentsService.Domain;

/// <summary>A named variable set in one workspace (CONTRACTS §3). Every row carries its workspace (P3, CONTRACTS §0).</summary>
public sealed class Environment
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The <c>sub</c> claim of the user who created the environment.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<Variable> Variables { get; set; } = [];
}
