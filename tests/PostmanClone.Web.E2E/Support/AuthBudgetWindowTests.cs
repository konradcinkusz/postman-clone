using Microsoft.Extensions.Time.Testing;

namespace PostmanClone.Web.E2E.Support;

/// <summary>The sign-in budget's window, on a fake clock anchored to now: calls within the limit pass, the next one waits for the window to end (OI-26).</summary>
public sealed class AuthBudgetWindowTests
{
    [Fact]
    public async Task Calls_within_the_limit_pass_at_once_and_the_next_one_waits_for_the_window_to_end()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var window = new AuthBudgetWindow(clock, limit: 3);
        var cancellation = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            await window.AcquireAsync(cancellation);
        }

        var fourth = window.AcquireAsync(cancellation);
        await Task.Yield();
        Assert.False(fourth.IsCompleted, "The fourth call in the window must wait.");

        // Thirty seconds into the window is not the end of it.
        clock.Advance(TimeSpan.FromSeconds(30));
        await Task.Yield();
        Assert.False(fourth.IsCompleted, "The window has not ended after thirty seconds.");

        clock.Advance(TimeSpan.FromSeconds(31));
        await fourth.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
    }

    [Fact]
    public async Task A_new_window_starts_after_the_oldest_call_ages_out()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var window = new AuthBudgetWindow(clock, limit: 2);
        var cancellation = TestContext.Current.CancellationToken;

        await window.AcquireAsync(cancellation);
        clock.Advance(TimeSpan.FromSeconds(40));
        await window.AcquireAsync(cancellation);

        // The first call is 40 seconds old: the second still counts, so the third waits for the first to age out.
        var third = window.AcquireAsync(cancellation);
        await Task.Yield();
        Assert.False(third.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(21));
        await third.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
    }

    [Fact]
    public void The_shared_budget_is_two_under_authservices_limit()
    {
        Assert.Equal(18, AuthBudget.PerWindow);
    }

    [Fact]
    public async Task Calls_already_spent_before_the_suite_count_against_the_first_window()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var window = new AuthBudgetWindow(clock, limit: 3, alreadySpent: 2);
        var cancellation = TestContext.Current.CancellationToken;

        await window.AcquireAsync(cancellation); // the one call that still fits
        var next = window.AcquireAsync(cancellation);
        await Task.Yield();
        Assert.False(next.IsCompleted, "Two spent calls plus one leave no room: the next call waits.");

        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        await next.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
        Assert.True(next.IsCompletedSuccessfully);
    }
}
