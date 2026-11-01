namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// Keeps the suite inside authservice's sign-in window. authservice v0.3.4 allows 20 auth requests per minute per client IP
/// (Program.cs: the "auth" policy, PermitLimit 20, a fixed one-minute window, a queue of 5). Every journey calls from the same
/// address, so a request past the limit waits in authservice for up to a minute. The suite counts its own register, login and
/// logout calls and, when the window is full, waits until the oldest counted call leaves it. The wait is the window's end,
/// not a guess. The budget is 18 (two under the limit) because the window is fixed, not sliding.
/// </summary>
public static class AuthBudget
{
    public const int PerWindow = 18;

    private static readonly AuthBudgetWindow Shared = new(TimeProvider.System, PerWindow);

    /// <summary>Waits, if needed, until one more auth call fits in the shared window, then counts it.</summary>
    public static Task AcquireAsync(CancellationToken cancellationToken) => Shared.AcquireAsync(cancellationToken);
}

/// <summary>
/// The budget's arithmetic, with the clock injected (TimeProvider, P-rules) so a test can move time: a fixed window of one
/// minute, at most <see cref="Limit"/> counted calls in it. A call past the limit waits for the oldest counted call to age out.
/// </summary>
public sealed class AuthBudgetWindow(TimeProvider clock, int limit)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly Queue<DateTimeOffset> _counted = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int Limit { get; } = limit;

    public async Task AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var now = clock.GetUtcNow();
                while (_counted.Count > 0 && now - _counted.Peek() >= Window)
                {
                    _counted.Dequeue();
                }

                if (_counted.Count < Limit)
                {
                    _counted.Enqueue(now);
                    return;
                }

                wait = Window - (now - _counted.Peek());
            }
            finally
            {
                _gate.Release();
            }

            // The wait is the window's end plus a little, read from the same clock the window uses.
            await Task.Delay(wait + TimeSpan.FromMilliseconds(100), clock, cancellationToken);
        }
    }
}
