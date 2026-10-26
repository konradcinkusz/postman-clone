using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PostmanClone.ServiceDefaults.Problems;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Problems;

/// <summary>Every error is <c>application/problem+json</c> with the CONTRACTS §0 <c>code</c> (a stable snake_case value).</summary>
public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task A_409_problem_carries_the_conflict_code_in_problem_json()
    {
        await using var app = await TestHost.StartAsync(map: app =>
            app.MapGet("/conflict", () => Results.Problem(statusCode: StatusCodes.Status409Conflict)));
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/conflict", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("conflict", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task A_validation_problem_carries_validation_failed_and_field_errors()
    {
        await using var app = await TestHost.StartAsync(map: app =>
            app.MapGet("/validation", () => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["name"] = ["Name is required."] })));
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/validation", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Name is required.", body.RootElement.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task An_endpoint_specific_code_is_kept_and_not_overwritten_by_the_status_default()
    {
        await using var app = await TestHost.StartAsync(map: app =>
            app.MapGet("/literal", () => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { [ProblemDetailsExtensions.CodeExtension] = "secret_literal_not_allowed" })));
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/literal", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("secret_literal_not_allowed", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unmatched_route_answers_404_not_found_as_problem_json()
    {
        await using var app = await TestHost.StartAsync();
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/no-such-route", TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("not_found", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unhandled_exception_answers_500_as_problem_json_without_leaking_its_message()
    {
        await using var app = await TestHost.StartAsync(map: app =>
            app.MapGet("/boom", () => { throw new InvalidOperationException("internal detail"); }));
        using var client = TestHost.ClientFor(app);

        using var response = await client.GetAsync("/boom", TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("internal detail", text);
    }
}
