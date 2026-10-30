using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Buffers.Text;

namespace PostmanClone.Web.Auth;

/// <summary>The sessions the Web holds, looked up by the id in the cookie (ADR-0005, ADR-0008, ADR-0013).</summary>
public interface ISessionStore
{
    ServerSession Create(SessionTokens tokens, IReadOnlyList<Claim> claims);

    /// <summary>The live session with this id, or null when it is unknown, expired or ended. <paramref name="touch"/> counts as user activity.</summary>
    ServerSession? Find(string id, bool touch);

    void Remove(string id);
}

/// <summary>
/// The in-memory session store. A session ends after eight hours without a request (sliding) or at its absolute
/// lifetime, which equals authservice's refresh-token lifetime: after that no refresh can succeed anyway. The store
/// holds no timer; expired entries are dropped when a session is looked up or created.
/// </summary>
public sealed class ServerSessionStore(TimeProvider clock) : ISessionStore
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(8);

    /// <summary>authservice's default refresh-token lifetime (docs/AUTH.md, Limits: seven days).</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, ServerSession> _sessions = new(StringComparer.Ordinal);

    public ServerSession Create(SessionTokens tokens, IReadOnlyList<Claim> claims)
    {
        RemoveExpired();
        var now = clock.GetUtcNow();
        var session = new ServerSession(NewId(), tokens, claims, now + AbsoluteLifetime, now);
        _sessions[session.Id] = session;
        return session;
    }

    public ServerSession? Find(string id, bool touch)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (IsExpired(session, now))
        {
            _sessions.TryRemove(id, out _);
            return null;
        }

        if (touch)
        {
            session.LastAccessedAt = now;
        }

        return session;
    }

    public void Remove(string id) => _sessions.TryRemove(id, out _);

    /// <summary>A 256-bit random id, base64url: 43 characters, and no JWT shape (three segments) is possible.</summary>
    internal static string NewId() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private bool IsExpired(ServerSession session, DateTimeOffset now) =>
        now >= session.AbsoluteExpiresAt || now - session.LastAccessedAt >= IdleTimeout;

    private void RemoveExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var (id, session) in _sessions)
        {
            if (IsExpired(session, now))
            {
                _sessions.TryRemove(id, out _);
            }
        }
    }
}
