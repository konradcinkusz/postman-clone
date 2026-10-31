using PostmanClone.CollectionsService.Domain;

namespace PostmanClone.CollectionsService.Services;

/// <summary>The refusals of the collections API, each with the status and code CONTRACTS §0 and §2 name.</summary>
public static class Failures
{
    /// <summary>Absent and foreign rows look the same (CONTRACTS §0): never 403 for a row of another workspace.</summary>
    public static Failure NotFound() => new(StatusCodes.Status404NotFound, "not_found", "The resource was not found.");

    public static Failure Forbidden(string title) => new(StatusCodes.Status403Forbidden, "forbidden", title);

    public static Failure Conflict(string title) => new(StatusCodes.Status409Conflict, "conflict", title);

    /// <summary>A size limit of CONTRACTS §2. No code of its own exists, so it answers with <c>conflict</c>.</summary>
    public static Failure LimitReached(string title) => Conflict(title);

    public static Failure DepthExceeded() => new(
        StatusCodes.Status400BadRequest,
        "depth_exceeded",
        $"Folders nest at most {CollectionLimits.MaxFolderDepth} levels deep.");

    public static Failure CycleDetected() => new(
        StatusCodes.Status400BadRequest,
        "cycle_detected",
        "A folder cannot be moved into itself or into one of its own subfolders.");

    public static Failure Invalid(string field, string message) => new(
        StatusCodes.Status400BadRequest,
        "validation_failed",
        "One or more values are invalid.",
        new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>A request definition over <see cref="RequestDefinitionLimits.MaxJsonBytes"/> (CONTRACTS §2: 413).</summary>
    public static Failure PayloadTooLarge() => new(
        StatusCodes.Status413PayloadTooLarge,
        "payload_too_large",
        $"The request definition is larger than {RequestDefinitionLimits.MaxJsonBytes} bytes of JSON.");

    /// <summary>
    /// The refusal for a definition that broke a rule. A secret literal answers <c>secret_literal_not_allowed</c>
    /// (CONTRACTS §1); every other rule answers <c>validation_failed</c>. Either way the messages are named by field.
    /// </summary>
    public static Failure FromIssues(RequestValidation validation)
    {
        var errors = validation.Issues
            .GroupBy(issue => issue.Field)
            .ToDictionary(group => group.Key, group => group.Select(issue => issue.Message).ToArray());

        return validation.HasSecretLiteral
            ? new Failure(StatusCodes.Status400BadRequest, RequestDefinitionValidator.SecretLiteralCode,
                "A secret field must hold a {{variable}} reference, not a literal value.", errors)
            : new Failure(StatusCodes.Status400BadRequest, RequestDefinitionValidator.InvalidCode,
                "One or more values are invalid.", errors);
    }
}
