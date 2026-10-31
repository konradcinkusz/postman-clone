using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PostmanClone.TestKit;
using PostmanClone.Web.Api;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Environments;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.Membership;

namespace PostmanClone.Web.Tests.EnvironmentsUi;

/// <summary>The environments client and service against the fake (WP21): outcomes, codes, field messages and the write-only rule.</summary>
public sealed class EnvironmentsClientTests
{
    private const string Bearer = "test-access-token";

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private const string StoredSentinel = "client-stored-sentinel-0931"; // gitleaks:allow

    [Fact]
    public async Task A_secret_value_is_never_in_what_the_client_returns()
    {
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging", new FakeEnvironmentsService.FakeVariable("token", StoredSentinel, IsSecret: true));
        var client = EnvironmentsRig.CreateClient(fake);

        var answer = await client.GetEnvironmentAsync(Bearer, id, Ct);

        var variable = Assert.Single(answer.Value!.Variables);
        Assert.True(variable.IsSecret);
        Assert.Null(variable.Value);
        Assert.True(variable.HasValue);
        Assert.DoesNotContain(StoredSentinel, JsonSerializer.Serialize(answer.Value));
    }

    [Fact]
    public async Task A_save_that_keeps_a_secret_sends_no_value_for_it()
    {
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging", new FakeEnvironmentsService.FakeVariable("token", StoredSentinel, IsSecret: true));
        var client = EnvironmentsRig.CreateClient(fake);

        var answer = await client.UpdateEnvironmentAsync(
            Bearer,
            id,
            new UpdateEnvironmentBody("Staging", [new VariableWriteBody("token", null, IsSecret: true, Enabled: true, KeepExistingSecret: true)]),
            Ct);

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        var sent = JsonNode.Parse(Assert.Single(fake.Bodies))!["variables"]![0]!;
        Assert.Equal("token", sent["key"]!.GetValue<string>());
        Assert.True(sent["keepExistingSecret"]!.GetValue<bool>());
        Assert.Null(sent["value"]);
        Assert.True(fake.StoredHasSecret(id, "token"));
    }

    [Fact]
    public async Task A_refused_save_carries_the_service_messages_by_field()
    {
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging");
        var client = EnvironmentsRig.CreateClient(fake);

        var answer = await client.UpdateEnvironmentAsync(
            Bearer,
            id,
            new UpdateEnvironmentBody("Staging", [new VariableWriteBody("token", null, IsSecret: true, Enabled: true, KeepExistingSecret: false)]),
            Ct);

        Assert.Equal(CollectionOutcome.Invalid, answer.Outcome);
        Assert.Equal("token: a secret needs a value, or keepExistingSecret when one is already stored.", Assert.Single(answer.Errors!["variables"]));
    }

    [Fact]
    public async Task A_missing_or_foreign_environment_is_not_found_with_its_code()
    {
        var fake = new FakeEnvironmentsService();
        var foreign = fake.AddForeignEnvironment("Other workspace");
        var client = EnvironmentsRig.CreateClient(fake);

        var foreignAnswer = await client.GetEnvironmentAsync(Bearer, foreign, Ct);
        var absentAnswer = await client.GetEnvironmentAsync(Bearer, Guid.NewGuid(), Ct);

        Assert.Equal(CollectionOutcome.NotFound, foreignAnswer.Outcome);
        Assert.Equal("not_found", foreignAnswer.Code);
        Assert.Equal(foreignAnswer.Outcome, absentAnswer.Outcome);
        Assert.Equal(foreignAnswer.Code, absentAnswer.Code);
    }

    [Fact]
    public async Task A_typed_secret_with_no_key_on_the_service_is_unavailable_with_its_code()
    {
        var fake = new FakeEnvironmentsService { SecretsUnavailable = true };
        var id = fake.AddEnvironment("Staging");
        var client = EnvironmentsRig.CreateClient(fake);

        var answer = await client.UpdateEnvironmentAsync(
            Bearer,
            id,
            new UpdateEnvironmentBody("Staging", [new VariableWriteBody("token", "typed-value-for-client", IsSecret: true, Enabled: true, KeepExistingSecret: false)]), // gitleaks:allow
            Ct);

        Assert.Equal(CollectionOutcome.Unavailable, answer.Outcome);
        Assert.Equal("secrets_unavailable", answer.Code);
        Assert.False(fake.StoredHasSecret(id, "token"));
    }

    [Fact]
    public async Task A_create_refused_at_the_limit_is_a_conflict_with_no_code()
    {
        var fake = new FakeEnvironmentsService();
        for (var index = 0; index < EnvironmentRules.EnvironmentsPerWorkspace; index++)
        {
            fake.AddEnvironment($"Env {index:D2}");
        }

        var answer = await EnvironmentsRig.CreateClient(fake).CreateEnvironmentAsync(Bearer, new CreateEnvironmentBody("One more"), Ct);

        Assert.Equal(CollectionOutcome.Conflict, answer.Outcome);
        Assert.Null(answer.Code);
        Assert.Equal(EnvironmentRules.EnvironmentsPerWorkspace, fake.Count);
    }

    [Fact]
    public async Task A_forbidden_delete_is_forbidden_and_the_environment_stays()
    {
        var fake = new FakeEnvironmentsService { ForbidDelete = true };
        var id = fake.AddEnvironment("Staging");

        var answer = await EnvironmentsRig.CreateClient(fake).DeleteEnvironmentAsync(Bearer, id, Ct);

        Assert.Equal(CollectionOutcome.Forbidden, answer.Outcome);
        Assert.Equal(1, fake.Count);
    }

    [Fact]
    public async Task A_stopped_service_is_unavailable_and_a_dropped_connection_is_unavailable_too()
    {
        var fake = new FakeEnvironmentsService { Unavailable = true };
        var stopped = await EnvironmentsRig.CreateClient(fake).ListEnvironmentsAsync(Bearer, 1, 100, Ct);

        var dropped = await DroppedClient().ListEnvironmentsAsync(Bearer, 1, 100, Ct);

        Assert.Equal(CollectionOutcome.Unavailable, stopped.Outcome);
        Assert.Equal(CollectionOutcome.Unavailable, dropped.Outcome);
    }

    [Fact]
    public async Task The_list_is_read_from_the_environments_route_with_the_paging_asked_for()
    {
        var fake = new FakeEnvironmentsService();
        fake.AddEnvironment("Billing");
        fake.AddEnvironment("Staging");

        var answer = await EnvironmentsRig.CreateClient(fake).ListEnvironmentsAsync(Bearer, 1, 100, Ct);

        Assert.Equal(2, answer.Value!.Total);
        Assert.Equal(new[] { "Billing", "Staging" }, answer.Value.Items.Select(item => item.Name));
        Assert.Contains("GET /api/v1/environments", fake.Calls);
    }

    [Fact]
    public async Task A_save_with_no_variables_removes_them_all()
    {
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging", new FakeEnvironmentsService.FakeVariable("token", StoredSentinel, IsSecret: true));
        var client = EnvironmentsRig.CreateClient(fake);

        var answer = await client.UpdateEnvironmentAsync(
            Bearer,
            id,
            new UpdateEnvironmentBody("Staging", []),
            Ct);

        Assert.Equal(CollectionOutcome.Succeeded, answer.Outcome);
        Assert.Empty(answer.Value!.Variables);
    }

    [Fact]
    public async Task The_service_keeps_the_chosen_environment_in_step_with_a_save_and_a_delete()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var id = fake.AddEnvironment("Staging", new FakeEnvironmentsService.FakeVariable("baseUrl", "https://staging.example"));
        using var rig = EnvironmentsRig.Create(harness, principal, fake);

        await rig.Service.ListAsync(Ct);
        var chosen = await rig.Service.ChooseAsync(id, Ct);
        Assert.Equal(CollectionOutcome.Succeeded, chosen.Outcome);
        Assert.Equal(new[] { "baseUrl" }, rig.Selection.KnownKeys!.Order());

        var editor = EnvironmentEditor.From(chosen.Value!);
        editor.Name = "Staging renamed";
        editor.AddRow();
        editor.Rows[1].Key = "region";
        editor.Rows[1].Value = "eu";
        var saved = await rig.Service.SaveAsync(editor, Ct);

        Assert.Equal(CollectionOutcome.Succeeded, saved.Outcome);
        Assert.Equal(rig.Selection.EnvironmentId, id);
        Assert.Equal("Staging renamed", rig.Selection.Name);
        Assert.Equal(new[] { "baseUrl", "region" }, rig.Selection.KnownKeys!.Order());

        var deleted = await rig.Service.DeleteAsync(id, Ct);
        Assert.Equal(CollectionOutcome.Succeeded, deleted.Outcome);
        Assert.Null(rig.Selection.EnvironmentId);
        Assert.Null(rig.Selection.KnownKeys);
    }

    [Fact]
    public async Task Choosing_an_environment_that_is_gone_clears_the_choice()
    {
        using var harness = new AuthHarness();
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeEnvironmentsService();
        var foreign = fake.AddForeignEnvironment("Other");
        using var rig = EnvironmentsRig.Create(harness, principal, fake);
        rig.Selection.Select(foreign, "stale", new HashSet<string>(StringComparer.Ordinal) { "old" });

        var answer = await rig.Service.ChooseAsync(foreign, Ct);

        Assert.Equal(CollectionOutcome.NotFound, answer.Outcome);
        Assert.Null(rig.Selection.EnvironmentId);
        Assert.Null(rig.Selection.KnownKeys);
    }

    private static EnvironmentsClient DroppedClient()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(BackendServices.EnvironmentsName, client => client.BaseAddress = new Uri("http://environments-service"))
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection reset")));
        return new EnvironmentsClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }
}
