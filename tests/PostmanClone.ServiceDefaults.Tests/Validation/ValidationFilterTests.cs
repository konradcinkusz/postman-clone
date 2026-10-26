using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PostmanClone.ServiceDefaults.Validation;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Validation;

/// <summary>DataAnnotations on a minimal API model answer 400 <c>validation_failed</c> with the failures grouped by member.</summary>
public sealed class ValidationFilterTests
{
    [Fact]
    public async Task A_model_that_breaks_its_annotations_answers_400_validation_failed_by_member()
    {
        await using var app = await StartAsync();
        using var client = TestHost.ClientFor(app);

        using var response = await client.PostAsJsonAsync("/samples", new Sample(Name: string.Empty), TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task A_model_that_meets_its_annotations_reaches_the_handler()
    {
        await using var app = await StartAsync();
        using var client = TestHost.ClientFor(app);

        using var response = await client.PostAsJsonAsync("/samples", new Sample(Name: "short"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_value_past_its_length_limit_is_refused_with_the_same_code()
    {
        await using var app = await StartAsync();
        using var client = TestHost.ClientFor(app);

        using var response = await client.PostAsJsonAsync("/samples", new Sample(Name: "far too long a name"), TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", body.RootElement.GetProperty("code").GetString());
    }

    private static Task<WebApplication> StartAsync() =>
        TestHost.StartAsync(map: app => app.MapPost("/samples", (Sample sample) => Results.Ok()).ValidateDataAnnotations());

    public sealed record Sample([property: Required, StringLength(10)] string Name);
}
