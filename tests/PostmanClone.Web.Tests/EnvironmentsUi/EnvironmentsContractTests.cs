extern alias environments;

using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using PostmanClone.Web.Api;
using PostmanClone.Web.Environments;
using Service = environments::PostmanClone.EnvironmentsService.Api;
using ServiceLimits = environments::PostmanClone.EnvironmentsService.Environments.EnvironmentLimits;

namespace PostmanClone.Web.Tests.EnvironmentsUi;

/// <summary>
/// The Web's environment types still match EnvironmentsService (CONTRACTS §3, BLAZOR-WEB-APP §5), in both directions. The service's
/// real types are written as JSON and read through the copies (the trees must be equal). The copies' request bodies are read by the
/// service's own deserializer and checked by its own attributes. And the client's rules agree with the service's on the same inputs.
/// </summary>
public sealed class EnvironmentsContractTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_stored_environment_with_a_secret_reads_into_the_copies_and_writes_back_unchanged()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var service = new Service.EnvironmentDto(
            Guid.NewGuid(),
            "Staging",
            "owner-subject",
            now,
            now.AddMinutes(5),
            [
                new Service.VariableDto("baseUrl", "https://staging.example", IsSecret: false, HasValue: true, Enabled: true),
                new Service.VariableDto("token", null, IsSecret: true, HasValue: true, Enabled: false),
            ]);
        var original = JsonNode.Parse(JsonSerializer.Serialize(service, WebOptions));

        var copy = JsonSerializer.Deserialize<EnvironmentDto>(original!.ToJsonString(), WebOptions);
        var again = JsonNode.Parse(JsonSerializer.Serialize(copy, WebOptions));

        Assert.True(JsonNode.DeepEquals(original, again), $"The environment changed on the way through the copies:\n{original}\n{again}");
        var secret = Assert.Single(copy!.Variables, variable => variable.IsSecret);
        Assert.Null(secret.Value);
        Assert.True(secret.HasValue);
        Assert.False(secret.Enabled);
    }

    [Fact]
    public void A_page_of_summaries_reads_into_the_copies_and_keeps_its_paging()
    {
        var page = new Service.Page<Service.EnvironmentSummary>(
            [new Service.EnvironmentSummary(Guid.NewGuid(), "Billing", 3, DateTimeOffset.UnixEpoch)],
            PageNumber: 1,
            PageSize: 100,
            Total: 1);
        var original = JsonNode.Parse(JsonSerializer.Serialize(page, WebOptions));

        var copy = JsonSerializer.Deserialize<PageResult<EnvironmentSummary>>(original!.ToJsonString(), WebOptions);
        var again = JsonNode.Parse(JsonSerializer.Serialize(copy, WebOptions));

        Assert.True(JsonNode.DeepEquals(original, again));
        Assert.Equal(100, copy!.PageSize);
        Assert.Equal(3, copy.Items[0].VariableCount);
    }

    [Fact]
    public void A_create_body_is_read_by_the_service_as_its_own_request()
    {
        var wire = JsonSerializer.Serialize(new CreateEnvironmentBody("  Staging "), WebOptions);

        var request = JsonSerializer.Deserialize<Service.CreateEnvironmentRequest>(wire, WebOptions);

        Assert.Equal("  Staging ", request!.Name);
        Assert.Empty(Validate(request));
    }

    [Fact]
    public void An_update_body_keeps_a_stored_secret_and_carries_a_typed_one_as_the_service_reads_them()
    {
        var body = new UpdateEnvironmentBody(
            "Staging",
            [
                new VariableWriteBody("baseUrl", "https://staging.example", IsSecret: false, Enabled: true, KeepExistingSecret: false),
                new VariableWriteBody("token", null, IsSecret: true, Enabled: true, KeepExistingSecret: true),
                new VariableWriteBody("password", "typed-value-for-contract", IsSecret: true, Enabled: false, KeepExistingSecret: false), // gitleaks:allow
            ]);
        var wire = JsonSerializer.Serialize(body, WebOptions);

        var request = JsonSerializer.Deserialize<Service.UpdateEnvironmentRequest>(wire, WebOptions);
        Assert.NotNull(request);

        Assert.Empty(Validate(request));
        var kept = request.Variables[1];
        Assert.True(kept.IsSecret);
        Assert.True(kept.KeepExistingSecret);
        Assert.Null(kept.Value);
        var typed = request.Variables[2];
        Assert.True(typed.IsSecret);
        Assert.False(typed.KeepExistingSecret);
        Assert.Equal("typed-value-for-contract", typed.Value); // gitleaks:allow
        Assert.False(typed.Enabled);
    }

    [Fact]
    public void The_copies_refuse_the_same_inputs_the_service_refuses_for_names_and_keys()
    {
        foreach (var name in new[] { string.Empty, " ", "a", new string('a', 100), new string('a', 101), " padded " })
        {
            var serviceValid = Validate(new Service.CreateEnvironmentRequest { Name = name }).Count == 0;

            Assert.Equal(serviceValid, EnvironmentRules.NameProblem(name) is null);
        }

        foreach (var key in new[] { string.Empty, "a", "A_b.c-d", "has space", "naïve", "tab\tkey", new string('k', 100), new string('k', 101) })
        {
            var serviceValid = KeyErrors(key).Count == 0;

            Assert.Equal(serviceValid, EnvironmentRules.KeyProblem(key) is null);
        }
    }

    [Fact]
    public void A_key_with_a_trailing_newline_is_refused_by_both_sides()
    {
        // The service's attribute demands a match of the whole key, so "abc\n" is refused there; the Web's pattern is anchored to
        // match that rule. A plain '$' would accept it in the Web, which is the case this test pins.
        const string key = "abc\n";

        Assert.NotEmpty(KeyErrors(key));
        Assert.NotNull(EnvironmentRules.KeyProblem(key));
    }

    [Fact]
    public void A_value_at_the_limit_is_accepted_by_both_sides_and_one_past_it_refused_by_both()
    {
        foreach (var length in new[] { EnvironmentRules.ValueMaxLength, EnvironmentRules.ValueMaxLength + 1 })
        {
            var value = new string('v', length);
            var serviceValid = Validate(new Service.VariableWrite { Key = "k", Value = value }).Count == 0;

            Assert.Equal(serviceValid, EnvironmentRules.ValueProblem(value) is null);
        }
    }

    [Fact]
    public void The_limits_the_page_checks_are_the_ones_the_service_enforces()
    {
        Assert.Equal(ServiceLimits.NameMaxLength, EnvironmentRules.NameMaxLength);
        Assert.Equal(ServiceLimits.KeyMaxLength, EnvironmentRules.KeyMaxLength);
        Assert.Equal(ServiceLimits.VariableValueMaxLength, EnvironmentRules.ValueMaxLength);
        Assert.Equal(ServiceLimits.VariablesPerEnvironment, EnvironmentRules.VariablesPerEnvironment);
        Assert.Equal(ServiceLimits.EnvironmentsPerWorkspace, EnvironmentRules.EnvironmentsPerWorkspace);
    }

    private static List<ValidationResult> KeyErrors(string key) =>
        Validate(new Service.VariableWrite { Key = key, Value = "v" }).Where(result => result.MemberNames.Contains("Key")).ToList();

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
