using System.Security.Cryptography;

namespace PostmanClone.Web.Agent;

/// <summary>What a ticket gives back once: the pairing code and when the code stops working.</summary>
public sealed record PairingTicket(string Code, DateTimeOffset CodeExpiresAt);

/// <summary>
/// Hands a freshly created pairing code from the form post to the Agent page without putting it in a URL (ADR-0028). The post
/// stores the code under a random ticket and redirects with the ticket only. The page takes the code once, from the same
/// session that created it; a ticket of another session, a used ticket or an old one gives nothing. The store lives in this
/// process, which is correct because the Web runs as one instance (ADR-0008).
/// </summary>
public sealed class AgentPairingTickets(TimeProvider time)
{
    /// <summary>How long a ticket waits for the page to take it. Long enough for a redirect, short enough to be useless later.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>The most tickets held at once. The oldest is dropped first; a page that asks late gets nothing.</summary>
    public const int Capacity = 256;

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    private sealed record Entry(string SessionId, PairingTicket Pairing, DateTimeOffset ExpiresAt);

    /// <summary>Stores a code for the session and returns the ticket that names it.</summary>
    public string Issue(string sessionId, string code, DateTimeOffset codeExpiresAt)
    {
        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = time.GetUtcNow();
        lock (_gate)
        {
            Prune(now);
            while (_entries.Count >= Capacity)
            {
                var oldest = _entries.MinBy(pair => pair.Value.ExpiresAt).Key;
                _entries.Remove(oldest);
            }

            _entries[ticket] = new Entry(sessionId, new PairingTicket(code, codeExpiresAt), now + Lifetime);
        }

        return ticket;
    }

    /// <summary>
    /// Gives the code once, to the session that created it. A ticket of another session is refused and kept, so its own session can
    /// still take it. An expired code is not given.
    /// </summary>
    public PairingTicket? Take(string sessionId, string? ticket)
    {
        if (string.IsNullOrEmpty(ticket))
        {
            return null;
        }

        var now = time.GetUtcNow();
        lock (_gate)
        {
            Prune(now);
            if (!_entries.TryGetValue(ticket, out var entry) || !string.Equals(entry.SessionId, sessionId, StringComparison.Ordinal))
            {
                return null;
            }

            _entries.Remove(ticket);
            return entry.Pairing.CodeExpiresAt > now ? entry.Pairing : null;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
        {
            _entries.Remove(key);
        }
    }
}
