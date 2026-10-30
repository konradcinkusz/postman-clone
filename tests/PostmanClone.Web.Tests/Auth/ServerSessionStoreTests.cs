using System.Security.Claims;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.Web.Auth;

namespace PostmanClone.Web.Tests.Auth;

/// <summary>The in-memory store: the id's shape and the two lifetimes (ADR-0005, ADR-0013).</summary>
public sealed class ServerSessionStoreTests
{
    private static readonly SessionTokens Tokens = new("access-value", "refresh-value", DateTimeOffset.MaxValue);

    private static IReadOnlyList<Claim> Claims => [new Claim("email", "person@example.test")];

    [Fact]
    public void A_session_id_is_256_random_bits_in_base64url()
    {
        var store = new ServerSessionStore(new FakeTimeProvider(DateTimeOffset.UtcNow));

        var first = store.Create(Tokens, Claims).Id;
        var second = store.Create(Tokens, Claims).Id;

        Assert.Matches("^[A-Za-z0-9_-]{43}$", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_session_ends_after_eight_idle_hours_and_a_request_extends_it()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new ServerSessionStore(clock);
        var id = store.Create(Tokens, Claims).Id;

        clock.Advance(TimeSpan.FromHours(7) + TimeSpan.FromMinutes(59));
        Assert.NotNull(store.Find(id, touch: true));

        clock.Advance(TimeSpan.FromHours(7) + TimeSpan.FromMinutes(59));
        Assert.NotNull(store.Find(id, touch: true));

        clock.Advance(ServerSessionStore.IdleTimeout);
        Assert.Null(store.Find(id, touch: true));
    }

    [Fact]
    public void A_session_ends_at_its_absolute_lifetime_whatever_the_activity()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var store = new ServerSessionStore(clock);
        var id = store.Create(Tokens, Claims).Id;

        // Touched every six hours, inside the idle timeout, so only the absolute lifetime can end it.
        var elapsed = TimeSpan.Zero;
        while (elapsed + TimeSpan.FromHours(6) < ServerSessionStore.AbsoluteLifetime)
        {
            clock.Advance(TimeSpan.FromHours(6));
            elapsed += TimeSpan.FromHours(6);
            Assert.NotNull(store.Find(id, touch: true));
        }

        clock.Advance(ServerSessionStore.AbsoluteLifetime - elapsed);
        Assert.Null(store.Find(id, touch: true));
    }

    [Fact]
    public void A_removed_session_is_gone()
    {
        var store = new ServerSessionStore(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var id = store.Create(Tokens, Claims).Id;

        store.Remove(id);

        Assert.Null(store.Find(id, touch: false));
    }

    [Fact]
    public void Tokens_are_not_rendered_by_the_session_record()
    {
        var store = new ServerSessionStore(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var session = store.Create(Tokens, Claims);

        Assert.DoesNotContain("access-value", session.ToString());
        Assert.DoesNotContain("refresh-value", session.Tokens.ToString());
    }
}
