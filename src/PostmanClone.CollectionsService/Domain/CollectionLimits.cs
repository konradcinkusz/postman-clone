namespace PostmanClone.CollectionsService.Domain;

/// <summary>The sizes CONTRACTS §2 fixes. Exceeding one is refused, never truncated.</summary>
public static class CollectionLimits
{
    public const int MaxCollectionsPerWorkspace = 200;

    public const int MaxFoldersPerCollection = 500;

    public const int MaxRequestsPerCollection = 5000;

    /// <summary>Levels of folders: a top-level folder is level 1, its subfolder level 2, and so on.</summary>
    public const int MaxFolderDepth = 8;
}
