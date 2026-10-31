namespace PostmanClone.CollectionsService.Contracts;

/// <summary>
/// One thing an import changed or left out (CONTRACTS §2). <see cref="Code"/> is a stable snake_case code, <see cref="Path"/>
/// names the item in the file, and the message never repeats a value from the file.
/// </summary>
public sealed record ImportWarning(string Code, string Message, string Path);

/// <summary>What an import created (CONTRACTS §2). The cURL import answers with the same shape.</summary>
public sealed record ImportResult(
    Guid CollectionId,
    string CollectionName,
    int FoldersCreated,
    int RequestsCreated,
    IReadOnlyList<ImportWarning> Warnings);
