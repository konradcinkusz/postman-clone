using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PostmanClone.ExecutionService.Options;

/// <summary>
/// Checks <see cref="ExecutionOptions"/> at startup (<c>ValidateOnStart</c>), so a bad value stops the host before it
/// listens. The SSRF relaxations are refused outside Development; every message names the setting that is wrong.
/// </summary>
public sealed class ExecutionOptionsValidator(IHostEnvironment environment) : IValidateOptions<ExecutionOptions>
{
    /// <summary>The contract's ceiling for a redirect chain (CONTRACTS §4).</summary>
    public const int ContractMaxRedirects = 5;

    /// <summary>The contract's ceiling for a request timeout (CONTRACTS §4).</summary>
    public const int ContractMaxTimeoutSeconds = 60;

    public ValidateOptionsResult Validate(string? name, ExecutionOptions options)
    {
        var failures = new List<string>();

        // Only Development may relax the guard. Staging or any other name is refused too: a relaxed guard is a route to
        // the metadata address and the private network, and no deployed environment should have one (ADR-0011, WP28).
        if (!environment.IsDevelopment())
        {
            if (options.Ssrf.AllowLoopback)
            {
                failures.Add($"{ExecutionOptions.SectionName}:Ssrf:AllowLoopback must be false outside Development.");
            }

            if (options.Ssrf.AllowPrivateRanges)
            {
                failures.Add($"{ExecutionOptions.SectionName}:Ssrf:AllowPrivateRanges must be false outside Development.");
            }
        }

        RequirePositive(failures, nameof(ExecutionOptions.RequestBodyMaxBytes), options.RequestBodyMaxBytes);
        RequirePositive(failures, nameof(ExecutionOptions.MaxHeaders), options.MaxHeaders);
        RequirePositive(failures, nameof(ExecutionOptions.UrlMaxLength), options.UrlMaxLength);
        RequirePositive(failures, nameof(ExecutionOptions.ResponseReadCapBytes), options.ResponseReadCapBytes);
        RequirePositive(failures, nameof(ExecutionOptions.RefuseDeclaredLengthBytes), options.RefuseDeclaredLengthBytes);
        RequirePositive(failures, nameof(ExecutionOptions.HistoryRetentionPerUserWorkspace), options.HistoryRetentionPerUserWorkspace);
        RequirePositive(failures, nameof(ExecutionOptions.HistoryBodyMaxBytes), options.HistoryBodyMaxBytes);

        if (options.MaxTimeoutSeconds is < 1 or > ContractMaxTimeoutSeconds)
        {
            failures.Add($"{ExecutionOptions.SectionName}:MaxTimeoutSeconds must be between 1 and {ContractMaxTimeoutSeconds}.");
        }

        if (options.DefaultTimeoutSeconds < 1 || options.DefaultTimeoutSeconds > options.MaxTimeoutSeconds)
        {
            failures.Add($"{ExecutionOptions.SectionName}:DefaultTimeoutSeconds must be between 1 and MaxTimeoutSeconds.");
        }

        if (options.MaxRedirects is < 0 or > ContractMaxRedirects)
        {
            failures.Add($"{ExecutionOptions.SectionName}:MaxRedirects must be between 0 and {ContractMaxRedirects}.");
        }

        if (options.Ssrf.BlockedPorts.Any(port => port is < 1 or > 65535))
        {
            failures.Add($"{ExecutionOptions.SectionName}:Ssrf:BlockedPorts must contain only ports between 1 and 65535.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(List<string> failures, string key, long value)
    {
        if (value <= 0)
        {
            failures.Add($"{ExecutionOptions.SectionName}:{key} must be greater than zero.");
        }
    }
}
