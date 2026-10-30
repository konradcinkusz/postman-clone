using Microsoft.Extensions.Options;
using PostmanClone.TestKit;
using PostmanClone.ExecutionService.Options;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Options;

/// <summary>The sender's limits and the SSRF switches, checked the way the host checks them at startup.</summary>
public sealed class ExecutionOptionsValidatorTests
{
    [Fact]
    public void The_defaults_are_the_contract_limits()
    {
        var options = new ExecutionOptions();

        Assert.Equal(1_048_576, options.RequestBodyMaxBytes);
        Assert.Equal(100, options.MaxHeaders);
        Assert.Equal(8192, options.UrlMaxLength);
        Assert.Equal(5L * 1024 * 1024, options.ResponseReadCapBytes);
        Assert.Equal(50L * 1024 * 1024, options.RefuseDeclaredLengthBytes);
        Assert.Equal(30, options.DefaultTimeoutSeconds);
        Assert.Equal(60, options.MaxTimeoutSeconds);
        Assert.Equal(5, options.MaxRedirects);
        Assert.Equal(200, options.HistoryRetentionPerUserWorkspace);
        Assert.Equal(64 * 1024, options.HistoryBodyMaxBytes);
        Assert.False(options.Ssrf.AllowLoopback);
        Assert.False(options.Ssrf.AllowPrivateRanges);
    }

    [Fact]
    public void Production_with_the_defaults_is_accepted()
    {
        var result = Validate("Production", new ExecutionOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("AllowLoopback")]
    [InlineData("AllowPrivateRanges")]
    public void Production_refuses_each_SSRF_relaxation_and_names_the_setting(string setting)
    {
        var options = new ExecutionOptions();
        if (setting == "AllowLoopback")
        {
            options.Ssrf.AllowLoopback = true;
        }
        else
        {
            options.Ssrf.AllowPrivateRanges = true;
        }

        var result = Validate("Production", options);

        Assert.True(result.Failed);
        Assert.Single(result.Failures);
        Assert.Equal($"Execution:Ssrf:{setting} must be false in Production.", result.Failures.Single());
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void Outside_Production_the_loopback_relaxation_is_accepted(string environment)
    {
        var options = new ExecutionOptions();
        options.Ssrf.AllowLoopback = true;

        var result = Validate(environment, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(nameof(ExecutionOptions.RequestBodyMaxBytes))]
    [InlineData(nameof(ExecutionOptions.MaxHeaders))]
    [InlineData(nameof(ExecutionOptions.UrlMaxLength))]
    [InlineData(nameof(ExecutionOptions.ResponseReadCapBytes))]
    [InlineData(nameof(ExecutionOptions.RefuseDeclaredLengthBytes))]
    [InlineData(nameof(ExecutionOptions.HistoryRetentionPerUserWorkspace))]
    [InlineData(nameof(ExecutionOptions.HistoryBodyMaxBytes))]
    public void A_zero_or_negative_limit_is_refused_and_named(string setting)
    {
        var options = new ExecutionOptions();
        Set(options, setting, 0);

        var result = Validate("Development", options);

        Assert.Equal([$"Execution:{setting} must be greater than zero."], result.Failures);
    }

    [Fact]
    public void The_maximum_timeout_cannot_exceed_the_contract_ceiling()
    {
        var options = new ExecutionOptions { MaxTimeoutSeconds = ExecutionOptionsValidator.ContractMaxTimeoutSeconds + 1 };

        var result = Validate("Development", options);

        Assert.Equal(["Execution:MaxTimeoutSeconds must be between 1 and 60."], result.Failures);
    }

    [Fact]
    public void The_default_timeout_cannot_exceed_the_maximum()
    {
        var options = new ExecutionOptions { DefaultTimeoutSeconds = 45, MaxTimeoutSeconds = 40 };

        var result = Validate("Development", options);

        Assert.Equal(["Execution:DefaultTimeoutSeconds must be between 1 and MaxTimeoutSeconds."], result.Failures);
    }

    [Fact]
    public void The_redirect_limit_cannot_exceed_the_contract_ceiling()
    {
        var options = new ExecutionOptions { MaxRedirects = ExecutionOptionsValidator.ContractMaxRedirects + 1 };

        var result = Validate("Development", options);

        Assert.Equal(["Execution:MaxRedirects must be between 0 and 5."], result.Failures);
    }

    private static ValidateOptionsResult Validate(string environment, ExecutionOptions options) =>
        new ExecutionOptionsValidator(new FakeHostEnvironment(environment)).Validate(null, options);

    private static void Set(ExecutionOptions options, string property, int value)
    {
        var target = typeof(ExecutionOptions).GetProperty(property)
            ?? throw new InvalidOperationException($"No property {property}.");
        target.SetValue(options, Convert.ChangeType(value, target.PropertyType));
    }
}
