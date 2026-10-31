using System.Text.Json;
using PostmanClone.CollectionsService.Contracts;
using PostmanClone.CollectionsService.Domain;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.CollectionsService.Tests.Requests;

/// <summary>
/// The cases in <c>tests/Shared/validation-cases.json</c>, one list of <c>{input, expectedCode}</c> that the client
/// validator (WP19) reads too, so the two judge a definition the same way. A null code means the input is valid.
/// </summary>
public sealed class ValidationCasesTests
{
    private sealed record ValidationCase(RequestDefinition Input, string? ExpectedCode);

    [Fact]
    public void Every_shared_case_gives_exactly_its_expected_code()
    {
        var path = Path.Combine(RepoRoot.Find(), "tests", "Shared", "validation-cases.json");
        var cases = JsonSerializer.Deserialize<List<ValidationCase>>(File.ReadAllText(path), JsonSerializerOptions.Web)!;

        Assert.NotEmpty(cases);
        foreach (var testCase in cases)
        {
            var codes = RequestDefinitionValidator.Validate(testCase.Input).Issues.Select(issue => issue.Code).Distinct().ToArray();
            Assert.Equal(testCase.ExpectedCode is null ? Array.Empty<string>() : new[] { testCase.ExpectedCode }, codes);
        }
    }
}
