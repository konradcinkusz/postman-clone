using PostmanClone.EnvironmentsService.Api;
using Environment = PostmanClone.EnvironmentsService.Domain.Environment;
using Variable = PostmanClone.EnvironmentsService.Domain.Variable;

namespace PostmanClone.EnvironmentsService.Environments;

/// <summary>
/// Maps stored rows to the API shapes. The one place a secret's value leaves the storage model, and it never does:
/// a secret is returned with a null <see cref="VariableDto.Value"/> (CONTRACTS §3, ADR-0006).
/// </summary>
public static class EnvironmentMapping
{
    public static EnvironmentDto ToEnvironmentDto(Environment environment) => new(
        environment.Id,
        environment.Name,
        environment.CreatedBy,
        environment.CreatedAt,
        environment.UpdatedAt,
        environment.Variables.OrderBy(variable => variable.Key, StringComparer.Ordinal).Select(ToVariableDto).ToList());

    public static VariableDto ToVariableDto(Variable variable) => new(
        variable.Key,
        variable.IsSecret ? null : variable.Value,
        variable.IsSecret,
        variable.IsSecret ? variable.Ciphertext is not null : variable.Value is not null,
        variable.Enabled);
}
