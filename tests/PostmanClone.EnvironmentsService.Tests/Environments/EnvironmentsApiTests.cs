using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.EnvironmentsService.Api;
using PostmanClone.EnvironmentsService.Environments;
using PostmanClone.TestKit;
using Xunit;
using static PostmanClone.EnvironmentsService.Tests.Environments.EnvironmentsHost;

namespace PostmanClone.EnvironmentsService.Tests.Environments;

/// <summary>Create, read, replace, delete, paging, limits, validation and the workspace gate (CONTRACTS §3, §0).</summary>
public sealed class EnvironmentsApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_answers_201_with_a_location_the_creator_and_the_clock_time()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await EnvironmentsHost.StartAsync(clock: clock, cancellationToken: Ct);
        var caller = Caller.Member(Guid.NewGuid(), "creator-1");

        using var response = await host.SendAsync(HttpMethod.Post, EnvironmentsRoute, caller, new { name = "dev" }, Ct);
        var dto = await response.Content.ReadFromJsonAsync<EnvironmentDto>(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"{EnvironmentsRoute}/{dto!.Id}", response.Headers.Location?.ToString());
        Assert.Equal("dev", dto.Name);
        Assert.Equal("creator-1", dto.CreatedBy);
        Assert.Equal(clock.GetUtcNow(), dto.CreatedAt);
        Assert.Equal(clock.GetUtcNow(), dto.UpdatedAt);
        Assert.Empty(dto.Variables);
    }

    [Fact]
    public async Task Create_rejects_a_blank_name_with_400_validation_failed()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);

        using var response = await host.SendAsync(HttpMethod.Post, EnvironmentsRoute, Caller.Owner(Guid.NewGuid()), new { name = "   " }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await response.ProblemCodeAsync(Ct));
        Assert.Contains("name", await response.ErrorMembersAsync(Ct));
    }

    [Fact]
    public async Task Create_refuses_the_51st_environment_of_a_workspace_with_409_and_another_workspace_is_unaffected()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var full = Caller.Owner(Guid.NewGuid());
        for (var i = 0; i < EnvironmentLimits.EnvironmentsPerWorkspace; i++)
        {
            using var created = await host.SendAsync(HttpMethod.Post, EnvironmentsRoute, full, new { name = $"env-{i}" }, Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var refused = await host.SendAsync(HttpMethod.Post, EnvironmentsRoute, full, new { name = "one-too-many" }, Ct);
        using var other = await host.SendAsync(HttpMethod.Post, EnvironmentsRoute, Caller.Owner(Guid.NewGuid()), new { name = "elsewhere" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("conflict", await refused.ProblemCodeAsync(Ct));
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Fact]
    public async Task List_is_paged_and_holds_only_the_callers_workspace()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var mine = Caller.Member(Guid.NewGuid());
        var theirs = Caller.Member(Guid.NewGuid());
        for (var i = 0; i < 3; i++)
        {
            using var _ = await host.SendAsync(HttpMethod.Post, EnvironmentsRoute, mine, new { name = $"mine-{i}" }, Ct);
        }

        using var foreign = await host.SendAsync(HttpMethod.Post, EnvironmentsRoute, theirs, new { name = "theirs" }, Ct);
        var firstPage = await (await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}?page=1&pageSize=2", mine, cancellationToken: Ct))
            .Content.ReadFromJsonAsync<Page<EnvironmentSummary>>(Ct);
        var secondPage = await (await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}?page=2&pageSize=2", mine, cancellationToken: Ct))
            .Content.ReadFromJsonAsync<Page<EnvironmentSummary>>(Ct);

        Assert.Equal(3, firstPage!.Total);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.DoesNotContain(firstPage.Items.Concat(secondPage.Items), summary => summary.Name == "theirs");
    }

    [Fact]
    public async Task List_clamps_the_page_size_to_100_and_the_page_to_1()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);

        using var response = await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}?page=0&pageSize=1000", Caller.Owner(Guid.NewGuid()), cancellationToken: Ct);
        var page = await response.Content.ReadFromJsonAsync<Page<EnvironmentSummary>>(Ct);

        Assert.Equal(1, page!.PageNumber);
        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task Get_returns_the_variables_in_key_order()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var created = await CreateAsync(host, caller, "ordering");
        using var put = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new
        {
            name = "ordering",
            variables = new[] { PlainVariable("beta", "2"), PlainVariable("alpha", "1") },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var read = await (await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{created.Id}", caller, cancellationToken: Ct))
            .Content.ReadFromJsonAsync<EnvironmentDto>(Ct);

        Assert.Equal(new[] { "alpha", "beta" }, read!.Variables.Select(variable => variable.Key).ToArray());
    }

    [Fact]
    public async Task Put_replaces_the_variable_set_and_the_name_and_moves_updatedAt_forward()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await EnvironmentsHost.StartAsync(clock: clock, cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var created = await CreateAsync(host, caller, "before");
        await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new { name = "before", variables = new[] { PlainVariable("OLD", "1") } }, Ct);
        clock.Advance(TimeSpan.FromMinutes(5));

        using var response = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new
        {
            name = "after",
            variables = new[] { PlainVariable("NEW", "2") },
        }, Ct);
        var updated = await response.Content.ReadFromJsonAsync<EnvironmentDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("after", updated!.Name);
        Assert.Equal(new[] { "NEW" }, updated.Variables.Select(variable => variable.Key).ToArray());
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal(clock.GetUtcNow(), updated.UpdatedAt);
    }

    [Fact]
    public async Task Put_accepts_200_variables_and_refuses_201_with_400()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var created = await CreateAsync(host, caller, "limit");
        var atLimit = Enumerable.Range(0, EnvironmentLimits.VariablesPerEnvironment).Select(i => PlainVariable($"V{i}", "x")).ToArray();
        var overLimit = Enumerable.Range(0, EnvironmentLimits.VariablesPerEnvironment + 1).Select(i => PlainVariable($"V{i}", "x")).ToArray();

        using var accepted = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new { name = "limit", variables = atLimit }, Ct);
        using var refused = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new { name = "limit", variables = overLimit }, Ct);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("validation_failed", await refused.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task Put_refuses_a_repeated_key_with_409_and_allows_a_case_variant()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var created = await CreateAsync(host, caller, "keys");

        using var variant = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new
        {
            name = "keys",
            variables = new[] { PlainVariable("Api", "1"), PlainVariable("api", "2") },
        }, Ct);
        using var repeated = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new
        {
            name = "keys",
            variables = new[] { PlainVariable("API", "1"), PlainVariable("API", "2") },
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, variant.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal("conflict", await repeated.ProblemCodeAsync(Ct));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("slash/key")]
    [InlineData("")]
    public async Task Put_refuses_a_key_outside_the_allowed_characters_with_400(string key)
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var created = await CreateAsync(host, caller, "keys");

        using var response = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new
        {
            name = "keys",
            variables = new[] { PlainVariable(key, "1") },
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_accepts_a_value_of_8192_characters_and_refuses_8193()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var created = await CreateAsync(host, caller, "size");

        using var fits = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new
        {
            name = "size",
            variables = new[] { PlainVariable("BIG", new string('a', 8192)) },
        }, Ct);
        using var tooLong = await host.SendAsync(HttpMethod.Put, $"{EnvironmentsRoute}/{created.Id}", caller, new
        {
            name = "size",
            variables = new[] { PlainVariable("BIG", new string('a', 8193)) },
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, fits.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Delete_by_the_creator_answers_204_and_the_environment_is_gone()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var creator = Caller.Member(Guid.NewGuid(), "creator-1");
        var created = await CreateAsync(host, creator, "mine");

        using var deleted = await host.SendAsync(HttpMethod.Delete, $"{EnvironmentsRoute}/{created.Id}", creator, cancellationToken: Ct);
        using var after = await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{created.Id}", creator, cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_by_another_member_answers_403_forbidden_and_keeps_the_row()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var workspace = Guid.NewGuid();
        var created = await CreateAsync(host, Caller.Member(workspace, "creator-1"), "mine");

        using var refused = await host.SendAsync(HttpMethod.Delete, $"{EnvironmentsRoute}/{created.Id}", Caller.Member(workspace, "someone-else"), cancellationToken: Ct);
        using var stillThere = await host.SendAsync(HttpMethod.Get, $"{EnvironmentsRoute}/{created.Id}", Caller.Member(workspace, "someone-else"), cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("forbidden", await refused.ProblemCodeAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task Delete_by_an_admin_or_an_owner_answers_204_for_any_creator()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var workspace = Guid.NewGuid();
        var first = await CreateAsync(host, Caller.Member(workspace, "creator-1"), "one");
        var second = await CreateAsync(host, Caller.Member(workspace, "creator-2"), "two");

        using var byAdmin = await host.SendAsync(HttpMethod.Delete, $"{EnvironmentsRoute}/{first.Id}", Caller.Admin(workspace), cancellationToken: Ct);
        using var byOwner = await host.SendAsync(HttpMethod.Delete, $"{EnvironmentsRoute}/{second.Id}", Caller.Owner(workspace), cancellationToken: Ct);

        Assert.Equal(HttpStatusCode.NoContent, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
    }

    [Fact]
    public async Task A_token_with_no_organization_answers_403_no_active_workspace()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, EnvironmentsRoute)
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", host.Tokens.Issue()) },
        };

        using var response = await host.Client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("no_active_workspace", await response.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task A_token_naming_two_organizations_answers_403_multiple_workspaces()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var claims = TestTokens.Membership(Guid.NewGuid(), "Owner").Concat(TestTokens.Membership(Guid.NewGuid(), "Owner")).ToList();
        using var request = new HttpRequestMessage(HttpMethod.Get, EnvironmentsRoute)
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", host.Tokens.Issue(claims)) },
        };

        using var response = await host.Client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("multiple_workspaces", await response.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task An_unknown_environment_answers_404_not_found_on_every_id_endpoint()
    {
        await using var host = await EnvironmentsHost.StartAsync(cancellationToken: Ct);
        var caller = Caller.Owner(Guid.NewGuid());
        var missing = $"{EnvironmentsRoute}/{Guid.NewGuid()}";

        var answers = new[]
        {
            await host.SendAsync(HttpMethod.Get, missing, caller, cancellationToken: Ct),
            await host.SendAsync(HttpMethod.Put, missing, caller, new { name = "x", variables = Array.Empty<object>() }, Ct),
            await host.SendAsync(HttpMethod.Delete, missing, caller, cancellationToken: Ct),
            await host.SendAsync(HttpMethod.Post, $"{missing}/resolve", caller, cancellationToken: Ct),
        };
        foreach (var answer in answers)
        {
            Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
            Assert.Equal("not_found", await answer.ProblemCodeAsync(Ct));
            answer.Dispose();
        }
    }

    /// <summary>A plain variable in the wire shape of <c>VariableWrite</c>.</summary>
    internal static object PlainVariable(string key, string? value) => new { key, value, isSecret = false, enabled = true, keepExistingSecret = false };

    /// <summary>Creates an environment as the given caller and returns what the service answered.</summary>
    internal static async Task<EnvironmentDto> CreateAsync(EnvironmentsHost host, Caller caller, string name)
    {
        using var response = await host.SendAsync(HttpMethod.Post, EnvironmentsRoute, caller, new { name }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EnvironmentDto>(TestContext.Current.CancellationToken))!;
    }
}
