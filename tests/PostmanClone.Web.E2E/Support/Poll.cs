using System.Diagnostics;

namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// A bounded wait for a server-side effect that the page does not show (the run record the Web posts after a send). It asks
/// again every quarter second and stops at the timeout with a failure that names what it waited for. It is not a fixed sleep:
/// the first answer that is present ends the wait (E2E-ACCEPTANCE-TESTING).
/// </summary>
public static class Poll
{
    public static async Task<T> UntilAsync<T>(Func<Task<T?>> attempt, string waitingFor, CancellationToken cancellationToken, TimeSpan? timeout = null)
        where T : class
    {
        var limit = timeout ?? TimeSpan.FromSeconds(20);
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var value = await attempt();
            if (value is not null)
            {
                return value;
            }

            if (clock.Elapsed >= limit)
            {
                throw new TimeoutException($"Waited {limit.TotalSeconds:0} s for {waitingFor}, and it did not appear.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }
}
