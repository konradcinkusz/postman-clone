using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PostmanClone.EnvironmentsService.Api;
using PostmanClone.EnvironmentsService.Data;
using PostmanClone.EnvironmentsService.Environments;
using PostmanClone.EnvironmentsService.Secrets;
using Xunit;
using Environment = PostmanClone.EnvironmentsService.Domain.Environment;
using static PostmanClone.EnvironmentsService.Tests.Environments.SecretFixtures;

namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>
/// The variable rules without a host: the two-phase replace, and the per-variable checks the validation filter runs
/// before a handler (CONTRACTS §3). Each test asserts the stored state, not only the answer. Each test has its own
/// in-memory context, with the environment tracked in it, as the endpoint has.
/// </summary>
public sealed class VariableRulesTests
{
    private readonly AesGcmSecretProtector _protector = new(RandomNumberGenerator.GetBytes(AesGcmSecretProtector.KeySizeInBytes));
    private readonly EnvironmentsDbContext _db = new(new DbContextOptionsBuilder<EnvironmentsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public void A_new_secret_is_encrypted_and_bound_to_its_workspace_environment_and_key()
    {
        var environment = NewEnvironment();

        var violations = Apply(environment, [Secret("API_TOKEN", Plaintext)]);

        var stored = Assert.Single(environment.Variables);
        Assert.Empty(violations);
        Assert.Null(stored.Value);
        Assert.Equal(Plaintext, _protector.Unprotect(stored.Ciphertext!, SecretPlace.For(environment.WorkspaceId, environment.Id, "API_TOKEN")));
        Assert.ThrowsAny<CryptographicException>(() =>
            _protector.Unprotect(stored.Ciphertext!, SecretPlace.For(environment.WorkspaceId, environment.Id, "OTHER_KEY")));
        Assert.ThrowsAny<CryptographicException>(() =>
            _protector.Unprotect(stored.Ciphertext!, SecretPlace.For(Guid.NewGuid(), environment.Id, "API_TOKEN")));
    }

    [Fact]
    public void Keeping_a_stored_secret_leaves_its_ciphertext_byte_for_byte()
    {
        var environment = NewEnvironment();
        Apply(environment, [Secret("API_TOKEN", Plaintext)]);
        var before = environment.Variables.Single().Ciphertext;

        var violations = Apply(environment, [Secret("API_TOKEN", null, keepExistingSecret: true)]);

        Assert.Empty(violations);
        Assert.Equal(before, environment.Variables.Single().Ciphertext);
    }

    [Fact]
    public void A_variable_absent_from_the_request_is_removed()
    {
        var environment = NewEnvironment();
        Apply(environment, [Plain("KEEP", "1"), Plain("DROP", "2")]);

        Apply(environment, [Plain("KEEP", "1")]);

        Assert.Equal(new[] { "KEEP" }, environment.Variables.Select(variable => variable.Key).ToArray());
    }

    [Fact]
    public void Keys_are_compared_case_sensitively()
    {
        var environment = NewEnvironment();

        Apply(environment, [Plain("Api", "1"), Plain("api", "2")]);

        Assert.Equal(new[] { "Api", "api" }, environment.Variables.Select(variable => variable.Key).OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void A_rejected_replace_changes_nothing_at_all()
    {
        var environment = NewEnvironment();
        Apply(environment, [Plain("A", "1"), Secret("S", Plaintext)]);
        var ciphertext = environment.Variables.Single(variable => variable.Key == "S").Ciphertext;

        var violations = Apply(environment, [Plain("A", "2"), Plain("GONE", "x"), Secret("Z", null, keepExistingSecret: true)]);

        Assert.Contains(UpdateEnvironmentRequest.VariablesMember, violations.Keys);
        Assert.Equal(2, environment.Variables.Count);
        Assert.Equal("1", environment.Variables.Single(variable => variable.Key == "A").Value);
        Assert.Equal(ciphertext, environment.Variables.Single(variable => variable.Key == "S").Ciphertext);
    }

    [Fact]
    public void Keeping_a_plain_variable_as_a_secret_is_refused()
    {
        var environment = NewEnvironment();
        Apply(environment, [Plain("T", "visible")]);

        var violations = Apply(environment, [Secret("T", null, keepExistingSecret: true)]);

        Assert.Contains(UpdateEnvironmentRequest.VariablesMember, violations.Keys);
        Assert.Equal("visible", environment.Variables.Single().Value);
    }

    [Fact]
    public void A_secret_turned_plain_stores_the_value_and_drops_the_ciphertext()
    {
        var environment = NewEnvironment();
        Apply(environment, [Secret("T", Plaintext)]);

        var violations = Apply(environment, [Plain("T", "visible")]);

        var stored = Assert.Single(environment.Variables);
        Assert.Empty(violations);
        Assert.False(stored.IsSecret);
        Assert.Equal("visible", stored.Value);
        Assert.Null(stored.Ciphertext);
    }

    [Fact]
    public void A_plain_variable_turned_secret_drops_the_value_and_stores_the_ciphertext()
    {
        var environment = NewEnvironment();
        Apply(environment, [Plain("T", "visible")]);

        var violations = Apply(environment, [Secret("T", Plaintext)]);

        var stored = Assert.Single(environment.Variables);
        Assert.Empty(violations);
        Assert.True(stored.IsSecret);
        Assert.Null(stored.Value);
        Assert.StartsWith("v1:", stored.Ciphertext, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_refuses_a_plain_variable_without_a_value()
    {
        var results = Validate(new VariableWrite { Key = "A", Value = null, IsSecret = false });

        Assert.Contains(results, result => result.ErrorMessage!.Contains("plain variable needs a value", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_refuses_a_secret_without_a_value_unless_it_keeps_the_stored_one()
    {
        Assert.Contains(Validate(new VariableWrite { Key = "S", Value = null, IsSecret = true, KeepExistingSecret = false }),
            result => result.ErrorMessage!.Contains("a secret needs a value", StringComparison.Ordinal));
        Assert.Empty(Validate(new VariableWrite { Key = "S", Value = null, IsSecret = true, KeepExistingSecret = true }));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("slash/key")]
    [InlineData("unicode-ż")]
    public void Validation_refuses_a_key_outside_the_allowed_characters(string key)
    {
        Assert.NotEmpty(Validate(new VariableWrite { Key = key, Value = "1" }));
    }

    [Fact]
    public void Validation_accepts_a_value_of_8192_characters_and_refuses_8193()
    {
        Assert.Empty(Validate(new VariableWrite { Key = "V", Value = new string('a', EnvironmentLimits.VariableValueMaxLength) }));
        Assert.NotEmpty(Validate(new VariableWrite { Key = "V", Value = new string('a', EnvironmentLimits.VariableValueMaxLength + 1) }));
    }

    [Fact]
    public void The_request_refuses_201_variables_and_accepts_200()
    {
        var atLimit = Enumerable.Range(0, EnvironmentLimits.VariablesPerEnvironment).Select(i => Plain($"V{i}", "x")).ToList();
        var overLimit = Enumerable.Range(0, EnvironmentLimits.VariablesPerEnvironment + 1).Select(i => Plain($"V{i}", "x")).ToList();

        Assert.Empty(Validate(new UpdateEnvironmentRequest { Name = "limit", Variables = atLimit }));
        Assert.NotEmpty(Validate(new UpdateEnvironmentRequest { Name = "limit", Variables = overLimit }));
    }

    [Fact]
    public void The_request_reports_a_bad_variable_under_the_variables_member()
    {
        var results = Validate(new UpdateEnvironmentRequest { Name = "x", Variables = [Plain("bad key", "1")] });

        Assert.Equal(new[] { UpdateEnvironmentRequest.VariablesMember }, results.SelectMany(result => result.MemberNames).Distinct().ToArray());
    }

    /// <summary>A tracked environment with no variables, in this test's own store.</summary>
    private Environment NewEnvironment()
    {
        var environment = new Environment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Name = "unit",
            CreatedBy = "owner-user",
        };
        _db.Environments.Add(environment);
        _db.SaveChanges();
        return environment;
    }

    /// <summary>Replaces the variables as the endpoint does, then saves, so the next call reads what this one stored.</summary>
    private Dictionary<string, string[]> Apply(Environment environment, IReadOnlyList<VariableWrite> writes)
    {
        var violations = new VariableWriter(_protector).Replace(_db, environment, writes);
        _db.SaveChanges();
        return violations;
    }

    private static VariableWrite Plain(string key, string? value) => new() { Key = key, Value = value, IsSecret = false };

    private static VariableWrite Secret(string key, string? value, bool keepExistingSecret = false) =>
        new() { Key = key, Value = value, IsSecret = true, KeepExistingSecret = keepExistingSecret };

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
