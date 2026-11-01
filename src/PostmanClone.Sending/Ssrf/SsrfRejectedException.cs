namespace PostmanClone.Sending.Ssrf;

/// <summary>
/// Thrown from the connection layer when a destination is refused. It is raised inside the HTTP stack, so it reaches
/// the caller wrapped; <see cref="Find"/> recovers it. The message names the code, never the address.
/// </summary>
public sealed class SsrfRejectedException(string errorCode, string? category = null)
    : Exception($"The destination was refused ({errorCode}).")
{
    /// <summary>One of <see cref="SsrfErrorCodes"/>.</summary>
    public string ErrorCode { get; } = errorCode;

    /// <summary>The address class that matched (for example <c>private</c>), or null when none applies.</summary>
    public string? Category { get; } = category;

    /// <summary>Finds the rejection anywhere in an exception chain, or null when the chain holds none.</summary>
    public static SsrfRejectedException? Find(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SsrfRejectedException rejected)
            {
                return rejected;
            }
        }

        return null;
    }
}
