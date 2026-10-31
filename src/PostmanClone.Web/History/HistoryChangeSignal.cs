namespace PostmanClone.Web.History;

/// <summary>
/// Raised when the circuit's history may have changed, so the workspace home panel reloads (WP22). A subscriber unsubscribes when it
/// is disposed; a raise after that reaches nobody, so a disposed panel is never called.
/// </summary>
public interface IHistoryChanged
{
    event Action? Changed;
}

/// <summary>The circuit's signal (scoped): the page raises it when a send has been recorded, and the panel listens.</summary>
public sealed class HistoryChangeSignal : IHistoryChanged
{
    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}
