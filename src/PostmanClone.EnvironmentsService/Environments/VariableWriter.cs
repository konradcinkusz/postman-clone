using PostmanClone.EnvironmentsService.Api;
using PostmanClone.EnvironmentsService.Secrets;
using Environment = PostmanClone.EnvironmentsService.Domain.Environment;
using Variable = PostmanClone.EnvironmentsService.Domain.Variable;

namespace PostmanClone.EnvironmentsService.Environments;

/// <summary>
/// Replaces an environment's variable set with a <c>PUT</c>'s (CONTRACTS §3). It works in two phases: every write is
/// planned first, and only when all of them are allowed is anything changed. A rejected request leaves the stored
/// variables as they were.
/// </summary>
public sealed class VariableWriter(ISecretProtector protector)
{
    /// <summary>Returns the rule violations, keyed by member; an empty dictionary means the set was replaced.</summary>
    public Dictionary<string, string[]> Replace(Environment environment, IReadOnlyList<VariableWrite> writes)
    {
        var messages = new List<string>();
        var planned = new List<PlannedVariable>(writes.Count);

        foreach (var write in writes)
        {
            var stored = environment.Variables.FirstOrDefault(variable => string.Equals(variable.Key, write.Key, StringComparison.Ordinal));
            if (Plan(environment, write, stored, messages) is { } item)
            {
                planned.Add(item);
            }
        }

        if (messages.Count > 0)
        {
            return new Dictionary<string, string[]> { [UpdateEnvironmentRequest.VariablesMember] = messages.ToArray() };
        }

        environment.Variables.RemoveAll(variable =>
            !planned.Any(item => string.Equals(item.Write.Key, variable.Key, StringComparison.Ordinal)));

        foreach (var item in planned)
        {
            var variable = item.Stored ?? new Variable
            {
                Id = Guid.NewGuid(),
                WorkspaceId = environment.WorkspaceId,
                EnvironmentId = environment.Id,
                Key = item.Write.Key,
            };
            variable.IsSecret = item.Write.IsSecret;
            variable.Enabled = item.Write.Enabled;
            variable.Value = item.Value;
            variable.Ciphertext = item.Ciphertext;

            if (item.Stored is null)
            {
                environment.Variables.Add(variable);
            }
        }

        return [];
    }

    /// <summary>The planned state of one variable. Exactly one of <see cref="Value"/> and <see cref="Ciphertext"/> is set.</summary>
    private sealed record PlannedVariable(VariableWrite Write, Variable? Stored, string? Value, string? Ciphertext);

    private PlannedVariable? Plan(Environment environment, VariableWrite write, Variable? stored, List<string> messages)
    {
        if (!write.IsSecret)
        {
            if (write.Value is null)
            {
                messages.Add($"{write.Key}: a plain variable needs a value.");
                return null;
            }

            return new PlannedVariable(write, stored, write.Value, null);
        }

        if (write.Value is not null)
        {
            var place = SecretPlace.For(environment.WorkspaceId, environment.Id, write.Key);
            return new PlannedVariable(write, stored, null, protector.Protect(write.Value, place));
        }

        if (!write.KeepExistingSecret)
        {
            messages.Add($"{write.Key}: a secret needs a value, or keepExistingSecret when one is already stored.");
            return null;
        }

        // Keeping is possible only when the stored row is a secret. Its ciphertext is bound to this key, which is unchanged.
        if (stored is not { IsSecret: true, Ciphertext: not null })
        {
            messages.Add($"{write.Key}: there is no stored secret to keep; supply a value.");
            return null;
        }

        return new PlannedVariable(write, stored, null, stored.Ciphertext);
    }
}
