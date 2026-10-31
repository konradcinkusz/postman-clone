using PostmanClone.CollectionsService.Services;

namespace PostmanClone.CollectionsService.Interop.Postman;

/// <summary>
/// Stops an import at its first refusal. It never leaves the importer: <see cref="PostmanV2Importer.Parse"/> turns it into
/// the failure answer. Each refusal names the path of the value that broke the rule, never the value itself.
/// </summary>
internal sealed class ImportRefusal(Failure failure) : Exception("The Postman import was refused.")
{
    public Failure Failure { get; } = failure;

    public static ImportRefusal Invalid(string path, string message) => new(new Failure(
        StatusCodes.Status400BadRequest,
        PostmanImportFailures.InvalidCode,
        "The file is not a valid Postman collection.",
        new Dictionary<string, string[]> { [path] = [message] }));

    public static ImportRefusal TooLarge(string message) => new(new Failure(
        StatusCodes.Status413PayloadTooLarge,
        PostmanImportFailures.TooLargeCode,
        message));

    public static ImportRefusal FromErrors(IReadOnlyDictionary<string, string[]> errors) => new(new Failure(
        StatusCodes.Status400BadRequest,
        PostmanImportFailures.InvalidCode,
        "The file is not a valid Postman collection.",
        errors));
}

/// <summary>The two stable codes of an import refusal (CONTRACTS §0).</summary>
public static class PostmanImportFailures
{
    public const string InvalidCode = "import_invalid";

    public const string TooLargeCode = "import_too_large";
}
