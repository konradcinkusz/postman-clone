using Bunit;
using PostmanClone.ServiceDefaults.Authentication;
using PostmanClone.Web.Api;
using PostmanClone.Web.Components.Pages;
using PostmanClone.Web.Response;
using PostmanClone.Web.Tests.Auth;
using PostmanClone.Web.Tests.Collections;
using PostmanClone.Web.Tests.Membership;
using static PostmanClone.Web.Tests.Response.ResponseViewTests;

namespace PostmanClone.Web.Tests.Response;

/// <summary>
/// The Send button on the builder, on the workspace home (WP20 acceptance, bUnit): the draft is what reaches the sender, the answer
/// is shown for the request it was sent from, Cancel stops the call and leaves the page idle, and Send is disabled while the draft
/// has a problem the service would refuse.
/// </summary>
public sealed class SendFromBuilderTests
{
    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    /// <summary>Records every body and answers with the given result, or waits until its call is cancelled.</summary>
    private sealed class RecordingGateway(Func<CancellationToken, Task<CollectionAnswer<ExecutionResultDto>>> respond) : IExecutionGateway
    {
        public List<ExecuteBody> Bodies { get; } = [];

        public Task<CollectionAnswer<ExecutionResultDto>> ExecuteAsync(ExecuteBody body, CancellationToken cancellationToken)
        {
            Bodies.Add(body);
            return respond(cancellationToken);
        }
    }

    private static async Task<(BunitContext Context, IRenderedComponent<WorkspaceHome> Page, Guid Request)> OpenAsync(AuthHarness harness, IExecutionGateway gateway)
    {
        var principal = await MembershipPageTestHarness.SignedInAsync(harness, Guid.NewGuid(), WorkspaceRoles.Owner, Ct);
        var fake = new FakeCollectionsService(Guid.NewGuid());
        var collection = fake.AddCollection("Billing");
        var request = fake.AddRequest(collection, "List items", definition: new RequestDefinition(
            "GET",
            "https://api.example.test/items",
            [],
            [],
            new RequestBody("none", null, null, []),
            new RequestAuth("none", null, null, null, null, null, null)));
        var context = CollectionsPageHarness.Create(harness, principal, fake, gateway);
        var cut = context.Render<WorkspaceHome>();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-collection-{collection}']").Count == 1);
        cut.Find($"[data-testid='tree-toggle-{collection}']").Click();
        cut.WaitForState(() => cut.FindAll($"[data-testid='tree-request-{request}']").Count == 1);
        cut.Find($"[data-testid='tree-request-{request}']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='builder-send']").Count == 1);
        return (context, cut, request);
    }

    [Fact]
    public async Task Send_sends_the_draft_and_shows_the_answer_for_that_request()
    {
        using var harness = new AuthHarness();
        var gateway = new RecordingGateway(_ => Task.FromResult(new CollectionAnswer<ExecutionResultDto>(
            CollectionOutcome.Succeeded,
            Result(response: ResponseOf(status: 201, body: "{\"ok\":true}")),
            null)));
        var (context, cut, request) = await OpenAsync(harness, gateway);
        using var _ = context;

        cut.Find("[data-testid='builder-send']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='response-status']").Count == 1);
        Assert.Equal("201 OK", cut.Find("[data-testid='response-status']").TextContent);
        var body = Assert.Single(gateway.Bodies);
        Assert.Equal("https://api.example.test/items", body.Request.Url);
        Assert.Equal(request, body.SavedRequestId);
        Assert.Null(body.EnvironmentId);
    }

    [Fact]
    public async Task Cancel_stops_the_call_and_leaves_the_page_idle_to_send_again()
    {
        using var harness = new AuthHarness();
        var gateway = new RecordingGateway(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Succeeded, null, null);
        });
        var (context, cut, _) = await OpenAsync(harness, gateway);
        using var _ = context;

        cut.Find("[data-testid='builder-send']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='builder-cancel']").Count == 1);
        Assert.True(cut.Find("[data-testid='builder-send']").HasAttribute("disabled"));

        cut.Find("[data-testid='builder-cancel']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='response-cancelled']").Count == 1);
        Assert.Empty(cut.FindAll("[data-testid='builder-cancel']"));
        Assert.False(cut.Find("[data-testid='builder-send']").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("[role='alert']"));
    }

    [Fact]
    public async Task A_send_the_service_refuses_is_an_alert_with_its_own_words()
    {
        using var harness = new AuthHarness();
        var gateway = new RecordingGateway(_ => Task.FromResult(new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.RateLimited, null, "rate_limited")));
        var (context, cut, _) = await OpenAsync(harness, gateway);
        using var _ = context;

        cut.Find("[data-testid='builder-send']").Click();

        cut.WaitForState(() => cut.FindAll("[data-testid='response-error']").Count == 1);
        Assert.Contains("too fast", cut.Find("[data-testid='response-error']").TextContent);
    }

    [Fact]
    public async Task Send_is_disabled_while_the_draft_has_a_problem_the_service_would_refuse()
    {
        using var harness = new AuthHarness();
        var gateway = new RecordingGateway(_ => Task.FromResult(new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Succeeded, Result(response: ResponseOf()), null)));
        var (context, cut, _) = await OpenAsync(harness, gateway);
        using var _ = context;

        cut.Find("[data-testid='builder-url']").Input(string.Empty);

        cut.WaitForState(() => cut.FindAll("[data-testid='builder-send-blocked']").Count == 1);
        Assert.True(cut.Find("[data-testid='builder-send']").HasAttribute("disabled"));
        Assert.Empty(gateway.Bodies);
    }
}
