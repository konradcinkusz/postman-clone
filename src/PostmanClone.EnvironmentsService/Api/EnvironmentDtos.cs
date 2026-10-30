using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PostmanClone.EnvironmentsService.Environments;

namespace PostmanClone.EnvironmentsService.Api;

/// <summary>A list page (CONTRACTS §0). The JSON property is <c>page</c>; the C# name cannot repeat the type name.</summary>
public sealed record Page<T>(
    IReadOnlyList<T> Items,
    [property: JsonPropertyName("page")] int PageNumber,
    int PageSize,
    int Total);

/// <summary>One row of the environment list (CONTRACTS §3).</summary>
public sealed record EnvironmentSummary(Guid Id, string Name, int VariableCount, DateTimeOffset UpdatedAt);

/// <summary>An environment with its variables. Secret values are never in it (CONTRACTS §3).</summary>
public sealed record EnvironmentDto(
    Guid Id,
    string Name,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<VariableDto> Variables);

/// <summary>
/// One stored variable as a client may read it. <see cref="Value"/> is null for a secret, and <see cref="HasValue"/>
/// says whether a secret holds a value without revealing it.
/// </summary>
public sealed record VariableDto(string Key, string? Value, bool IsSecret, bool HasValue, bool Enabled);

/// <summary>The body of <c>POST /environments</c>.</summary>
public sealed class CreateEnvironmentRequest
{
    [Required]
    [StringLength(EnvironmentLimits.NameMaxLength, MinimumLength = 1)]
    [RegularExpression(@"[\s\S]*\S[\s\S]*", ErrorMessage = "The name must contain a non-space character.")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>The body of <c>PUT /environments/{id}</c>: a name and the complete variable set (CONTRACTS §3).</summary>
public sealed class UpdateEnvironmentRequest : IValidatableObject
{
    [Required]
    [StringLength(EnvironmentLimits.NameMaxLength, MinimumLength = 1)]
    [RegularExpression(@"[\s\S]*\S[\s\S]*", ErrorMessage = "The name must contain a non-space character.")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(EnvironmentLimits.VariablesPerEnvironment)]
    public List<VariableWrite> Variables { get; set; } = [];

    /// <summary>Checks each variable's own rules. The validation filter reads only this object, not the items.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var variable in Variables)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(variable, new ValidationContext(variable), results, validateAllProperties: true);
            foreach (var result in results)
            {
                yield return new ValidationResult(result.ErrorMessage, [VariablesMember]);
            }
        }
    }

    /// <summary>The member the variable errors are reported under.</summary>
    public const string VariablesMember = "variables";
}

/// <summary>One variable of a <c>PUT</c> (CONTRACTS §3). A secret without a value keeps the stored one only when asked to.</summary>
public sealed class VariableWrite : IValidatableObject
{
    [Required]
    [StringLength(EnvironmentLimits.KeyMaxLength, MinimumLength = 1)]
    [RegularExpression(@"^[A-Za-z0-9_.\-]+$", ErrorMessage = "The key may use only letters, digits, '_', '.' and '-'.")]
    public string Key { get; set; } = string.Empty;

    [StringLength(EnvironmentLimits.VariableValueMaxLength)]
    public string? Value { get; set; }

    public bool IsSecret { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>For a secret with no <see cref="Value"/>: keep the ciphertext already stored for this key.</summary>
    public bool KeepExistingSecret { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Value is null && !IsSecret)
        {
            yield return new ValidationResult($"{Key}: a plain variable needs a value.", [nameof(Value)]);
        }

        if (Value is null && IsSecret && !KeepExistingSecret)
        {
            yield return new ValidationResult(
                $"{Key}: a secret needs a value, or keepExistingSecret when one is already stored.",
                [nameof(Value)]);
        }
    }
}

/// <summary>The body of <c>POST /environments/{id}/resolve</c>: the enabled variables with their plaintext (ADR-0006).</summary>
public sealed record ResolvedEnvironment(Guid Id, IReadOnlyList<ResolvedVariable> Variables);

/// <summary>One resolved variable. This is the only response that carries a secret's plaintext.</summary>
public sealed record ResolvedVariable(string Key, string Value, bool IsSecret);
