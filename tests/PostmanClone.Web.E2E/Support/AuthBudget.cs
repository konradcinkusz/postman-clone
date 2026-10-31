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
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly Queue<DateTimeOffset> Counted = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Waits, if needed, until one more auth call fits in the window, then counts it.</summary>
    public static async Task AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan wait;
            await Gate.WaitAsync(cancellationToken);
            try
            {
                var now = TimeProvider.System.GetUtcNow();
                while (Counted.Count > 0 && now - Counted.Peek() >= Window)
                {
                    Counted.Dequeue();
                }

                if (Counted.Count < PerWindow)
                {
                    Counted.Enqueue(now);
                    return;
                }

                wait = Window - (now - Counted.Peek());
            }
            finally
            {
                Gate.Release();
            }

            await Task.Delay(wait + TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }
}
