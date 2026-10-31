using PostmanClone.Web.Api;
using PostmanClone.Web.Response;
using static PostmanClone.Web.Tests.Response.ResponseViewTests;

namespace PostmanClone.Web.Tests.Response;

/// <summary>
/// The send coordinator (WP20 acceptance): a stale answer never overwrites a newer one, cancelling reaches the call and leaves the
/// page idle, an unexpected failure is shown with no exception text, and the environment chosen is the one sent. The gateway is a
/// scripted fake, so each test decides when an answer arrives.
/// </summary>
public sealed class SendCoordinatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Request = Guid.NewGuid();

    private static RequestDefinition Definition() =>
        new("GET", "https://api.example.test/items", [], [], new RequestBody("none", null, null, []), new RequestAuth("none", null, null, null, null, null, null));

    private sealed class ScriptedGateway(Func<ExecuteBody, CancellationToken, Task<CollectionAnswer<ExecutionResultDto>>> respond) : IExecutionGateway
    {
        public List<ExecuteBody> Bodies { get; } = [];

        public Task<CollectionAnswer<ExecutionResultDto>> ExecuteAsync(ExecuteBody body, CancellationToken cancellationToken)
        {
            Bodies.Add(body);
            return respond(body, cancellationToken);
        }
    }

    private static CollectionAnswer<ExecutionResultDto> Answer(int status) =>
        new(CollectionOutcome.Succeeded, Result(response: ResponseOf(status: status)), null);

    [Fact]
    public async Task A_send_shows_the_response_for_its_request_and_sends_the_selected_environment()
    {
        var environment = new EnvironmentSelection();
        var chosen = Guid.NewGuid();
        environment.Select(chosen);
        var gateway = new ScriptedGateway((_, _) => Task.FromResult(Answer(201)));
        var coordinator = new SendCoordinator(gateway, environment);

        await coordinator.SendAsync(Request, Definition(), Request, Ct);

        Assert.Equal(SendPhase.Shown, coordinator.View.Phase);
        Assert.Equal(201, coordinator.View.Response!.StatusCode);
        Assert.Equal(Request, coordinator.View.RequestId);
        Assert.Equal(chosen, gateway.Bodies.Single().EnvironmentId);
        Assert.Equal(Request, gateway.Bodies.Single().SavedRequestId);
        Assert.False(coordinator.IsSending);
        Assert.Equal(SendView.Idle.Phase, coordinator.ViewFor(Guid.NewGuid()).Phase);
    }

    [Fact]
    public async Task With_no_environment_chosen_the_send_carries_none()
    {
        var gateway = new ScriptedGateway((_, _) => Task.FromResult(Answer(200)));
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection());

        await coordinator.SendAsync(Request, Definition(), null, Ct);

        Assert.Null(gateway.Bodies.Single().EnvironmentId);
    }

    [Fact]
    public async Task A_stale_answer_never_overwrites_a_newer_one()
    {
        var pending = new Queue<TaskCompletionSource<CollectionAnswer<ExecutionResultDto>>>();
        var gateway = new ScriptedGateway((_, _) =>
        {
            var answer = new TaskCompletionSource<CollectionAnswer<ExecutionResultDto>>();
            pending.Enqueue(answer);
            return answer.Task;
        });
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection());

        var first = coordinator.SendAsync(Request, Definition(), null, Ct);
        var second = coordinator.SendAsync(Request, Definition(), null, Ct);
        var firstAnswer = pending.Dequeue();
        var secondAnswer = pending.Dequeue();

        // The newer send answers first, then the older one arrives late and ignores its cancellation.
        secondAnswer.SetResult(Answer(202));
        await second;
        firstAnswer.SetResult(Answer(500));
        await first;

        Assert.Equal(202, coordinator.View.Response!.StatusCode);
    }

    [Fact]
    public async Task Cancelling_reaches_the_call_and_leaves_the_page_idle_with_no_exception()
    {
        var gateway = new ScriptedGateway(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Answer(200);
        });
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection());

        var send = coordinator.SendAsync(Request, Definition(), null, Ct);
        Assert.True(coordinator.IsSending);
        coordinator.Cancel();
        await send;

        Assert.False(coordinator.IsSending);
        Assert.Equal(SendPhase.Cancelled, coordinator.View.Phase);
        Assert.Null(coordinator.View.Response);
    }

    [Fact]
    public async Task Disposing_cancels_the_send_in_flight()
    {
        var gateway = new ScriptedGateway(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Answer(200);
        });
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection());

        var send = coordinator.SendAsync(Request, Definition(), null, Ct);
        coordinator.Dispose();
        await send;

        Assert.Equal(SendPhase.Cancelled, coordinator.View.Phase);
    }

    [Fact]
    public async Task After_dispose_a_send_does_not_call_the_gateway()
    {
        var gateway = new ScriptedGateway((_, _) => Task.FromResult(Answer(200)));
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection());
        coordinator.Dispose();

        await coordinator.SendAsync(Request, Definition(), null, Ct);

        Assert.Empty(gateway.Bodies);
    }

    [Fact]
    public async Task A_refused_send_shows_its_own_words_and_no_response()
    {
        var gateway = new ScriptedGateway((_, _) => Task.FromResult(new CollectionAnswer<ExecutionResultDto>(CollectionOutcome.Invalid, null, "secret_literal_not_allowed")));
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection());

        await coordinator.SendAsync(Request, Definition(), null, Ct);

        Assert.Equal(SendPhase.Failed, coordinator.View.Phase);
        Assert.Contains("{{variable}}", coordinator.View.Failure);
    }

    [Fact]
    public async Task An_unexpected_failure_is_shown_as_unavailable_with_no_exception_text()
    {
        var gateway = new ScriptedGateway((_, _) => throw new InvalidOperationException("internal detail boom"));
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection());

        await coordinator.SendAsync(Request, Definition(), null, Ct);

        Assert.Equal(SendPhase.Failed, coordinator.View.Phase);
        Assert.DoesNotContain("boom", coordinator.View.Failure);
        Assert.Contains("could not be reached", coordinator.View.Failure);
    }

    [Fact]
    public async Task Each_state_change_raises_the_changed_event()
    {
        var gateway = new ScriptedGateway((_, _) => Task.FromResult(Answer(200)));
        var coordinator = new SendCoordinator(gateway, new EnvironmentSelection());
        var phases = new List<SendPhase>();
        coordinator.Changed += () => phases.Add(coordinator.View.Phase);

        await coordinator.SendAsync(Request, Definition(), null, Ct);

        Assert.Equal([SendPhase.Sending, SendPhase.Shown], phases);
    }
}
