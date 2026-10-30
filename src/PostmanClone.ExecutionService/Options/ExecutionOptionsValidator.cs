using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PostmanClone.ExecutionService.Options;

/// <summary>
/// Checks <see cref="ExecutionOptions"/> at startup (<c>ValidateOnStart</c>), so a bad value stops the host before it
/// listens. The SSRF relaxations are refused in Production; every message names the setting that is wrong.
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

        if (environment.IsProduction())
        {
            if (options.Ssrf.AllowLoopback)
            {
                failures.Add($"{ExecutionOptions.SectionName}:Ssrf:AllowLoopback must be false in Production.");
            }

            if (options.Ssrf.AllowPrivateRanges)
            {
                failures.Add($"{ExecutionOptions.SectionName}:Ssrf:AllowPrivateRanges must be false in Production.");
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
