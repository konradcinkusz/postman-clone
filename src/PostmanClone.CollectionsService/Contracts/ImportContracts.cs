using System.ComponentModel.DataAnnotations;

namespace PostmanClone.CollectionsService.Contracts;

/// <summary>
/// One thing an import did not carry over (CONTRACTS §2): a stable <see cref="Code"/>, a message that never repeats a
/// value, and the <see cref="Path"/> of the item in the source (for example <c>command[3]</c>).
/// </summary>
public sealed record ImportWarning(string Code, string Message, string Path);

/// <summary>The outcome of an import (CONTRACTS §2). A cURL import creates one request and no folders.</summary>
public sealed record ImportResult(
    Guid CollectionId,
    string CollectionName,
    int FoldersCreated,
    int RequestsCreated,
    IReadOnlyList<ImportWarning> Warnings);

/// <summary>Imports one cURL command as a saved request in a collection, optionally inside a folder (CONTRACTS §2).</summary>
public sealed record CurlImportBody(
    [property: Required, StringLength(20000, MinimumLength = 1)] string Command,
    [property: Required] Guid CollectionId,
    Guid? FolderId);
