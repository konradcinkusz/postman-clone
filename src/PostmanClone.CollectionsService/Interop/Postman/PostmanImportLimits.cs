namespace PostmanClone.CollectionsService.Interop.Postman;

/// <summary>
/// The limits of a Postman import (ADR-0016). Each one is checked before the work it bounds, and a refusal names the limit.
/// The folder, request and collection counts are the ones CONTRACTS §2 fixes, held in <see cref="Domain.CollectionLimits"/>.
/// </summary>
public static class PostmanImportLimits
{
    /// <summary>The largest file accepted, in bytes (CONTRACTS §2: 5 MB).</summary>
    public const int MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>Room for the multipart envelope around a file of <see cref="MaxFileBytes"/>.</summary>
    public const int MaxMultipartEnvelopeBytes = 64 * 1024;

    /// <summary>The parser refuses JSON nested deeper than this, so a hostile depth never reaches the walker.</summary>
    public const int MaxJsonDepth = 64;

    /// <summary>Folder levels a file may nest, the top level counting as 1. Deeper is refused; levels past the model's depth are flattened.</summary>
    public const int MaxFolderNesting = 20;

    /// <summary>Folders and requests together in one file.</summary>
    public const int MaxItems = 5500;

    /// <summary>Warnings listed in one answer. The rest are counted in one summary warning.</summary>
    public const int MaxWarnings = 500;

    /// <summary>The longest folder or collection description kept; a longer one is cut with a warning.</summary>
    public const int MaxDescriptionLength = 500;
}
