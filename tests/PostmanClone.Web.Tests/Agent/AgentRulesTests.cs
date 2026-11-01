using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using PostmanClone.Web.Agent;
using PostmanClone.Web.Api;

namespace PostmanClone.Web.Tests.Agent;

/// <summary>The origins the page may reach (CONTRACTS A2.6): loopback http on the configured port, nothing else, read at startup.</summary>
public sealed class AgentWebOptionsTests
{
    private static AgentWebOptions From(params (string Key, string? Value)[] values) =>
        AgentWebOptions.From(new ConfigurationBuilder().AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))).Build());

    [Fact]
    public void Without_configuration_the_page_may_reach_both_loopback_names_on_the_default_port()
    {
        var options = From();

        Assert.Equal(47631, options.Port);
        Assert.Equal(["http://127.0.0.1:47631", "http://localhost:47631"], options.ConnectOrigins);
    }

    [Fact]
    public void The_port_and_the_origins_follow_the_configuration()
    {
        var options = From(("Agent:Port", "50123"));

        Assert.Equal(50123, options.Port);
        Assert.Equal(["http://127.0.0.1:50123", "http://localhost:50123"], options.ConnectOrigins);
    }

    [Theory]
    [InlineData("http://*:47631")]
    [InlineData("https://evil.example:47631")]
    [InlineData("http://192.168.1.20:47631")]
    [InlineData("http://127.0.0.1:47632")]
    [InlineData("https://127.0.0.1:47631")]
    [InlineData("http://127.0.0.1:47631/agent")]
    [InlineData("http://127.0.0.1")]
    public void An_origin_that_is_not_the_loopback_on_the_port_stops_the_host(string origin)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => From(("Agent:ConnectOrigins:0", origin)));

        Assert.Contains("Agent:ConnectOrigins", failure.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("many")]
    public void A_port_that_is_not_a_port_stops_the_host(string port)
    {
        Assert.Throws<InvalidOperationException>(() => From(("Agent:Port", port)));
    }

    [Fact]
    public void A_wildcard_is_never_a_connect_origin()
    {
        Assert.False(AgentWebOptions.IsLoopbackOrigin("*", 47631));
        Assert.False(AgentWebOptions.IsLoopbackOrigin("http://*", 47631));
    }
}

/// <summary>The top bar's state and the builder's default sender (WP31): pure rules, tested as a table.</summary>
public sealed class AgentStatusRulesTests
{
    [Theory]
    [InlineData(AgentProbe.Unchecked, false, AgentStatus.Checking)]
    [InlineData(AgentProbe.Unreachable, false, AgentStatus.NotRunning)]
    [InlineData(AgentProbe.Unreachable, true, AgentStatus.NotRunning)]
    [InlineData(AgentProbe.Refused, true, AgentStatus.NotPaired)]
    [InlineData(AgentProbe.NotPaired, false, AgentStatus.NotPaired)]
    [InlineData(AgentProbe.NotPaired, true, AgentStatus.NotPaired)]
    [InlineData(AgentProbe.Paired, true, AgentStatus.Connected)]
    [InlineData(AgentProbe.Paired, false, AgentStatus.PairAgain)]
    public void The_status_follows_the_probe_and_whether_this_page_holds_a_pairing(AgentProbe probe, bool pagePaired, AgentStatus expected) =>
        Assert.Equal(expected, AgentStatusRules.Derive(probe, pagePaired));

    [Theory]
    [InlineData(AgentProbe.Paired, true, SendTarget.Agent)]
    [InlineData(AgentProbe.Paired, false, SendTarget.Server)]
    [InlineData(AgentProbe.Unreachable, true, SendTarget.Server)]
    [InlineData(AgentProbe.NotPaired, true, SendTarget.Server)]
    [InlineData(AgentProbe.Unchecked, true, SendTarget.Server)]
    public void The_default_sender_is_the_agent_only_when_this_page_is_paired_and_the_agent_answered_as_paired(AgentProbe probe, bool pagePaired, SendTarget expected) =>
        Assert.Equal(expected, AgentStatusRules.EffectiveTarget(null, probe, pagePaired));

    [Fact]
    public void A_choice_the_user_made_wins_over_the_default()
    {
        Assert.Equal(SendTarget.Server, AgentStatusRules.EffectiveTarget(SendTarget.Server, AgentProbe.Paired, true));
        Assert.Equal(SendTarget.Agent, AgentStatusRules.EffectiveTarget(SendTarget.Agent, AgentProbe.Unreachable, false));
    }

    [Fact]
    public void Each_state_has_the_label_the_top_bar_shows()
    {
        Assert.Equal("Connected", AgentStatusRules.Label(AgentStatus.Connected));
        Assert.Equal("Not running", AgentStatusRules.Label(AgentStatus.NotRunning));
        Assert.Equal("Not paired", AgentStatusRules.Label(AgentStatus.NotPaired));
        Assert.Equal("Pair again", AgentStatusRules.Label(AgentStatus.PairAgain));
    }
}

/// <summary>The one-time ticket that carries a pairing code to the page without a URL (ADR-0028).</summary>
public sealed class AgentPairingTicketsTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_ticket_gives_its_code_once_to_the_session_that_made_it()
    {
        var time = new FakeTimeProvider(Start);
        var tickets = new AgentPairingTickets(time);
        var ticket = tickets.Issue("session-a", "ABCD234567", Start.AddMinutes(5));

        var first = tickets.Take("session-a", ticket);
        var second = tickets.Take("session-a", ticket);

        Assert.Equal("ABCD234567", first?.Code);
        Assert.Null(second);
    }

    [Fact]
    public void Another_session_gets_nothing_and_the_owner_can_still_take_it()
    {
        var time = new FakeTimeProvider(Start);
        var tickets = new AgentPairingTickets(time);
        var ticket = tickets.Issue("session-a", "ABCD234567", Start.AddMinutes(5));

        Assert.Null(tickets.Take("session-b", ticket));
        Assert.Equal("ABCD234567", tickets.Take("session-a", ticket)?.Code);
    }

    [Fact]
    public void A_ticket_expires_with_its_lifetime_and_a_code_expires_with_its_own_clock()
    {
        var time = new FakeTimeProvider(Start);
        var tickets = new AgentPairingTickets(time);
        var lateTicket = tickets.Issue("session-a", "ABCD234567", Start.AddMinutes(5));
        var shortCode = tickets.Issue("session-a", "ZZZZ234567", Start.AddSeconds(30));

        time.Advance(AgentPairingTickets.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(tickets.Take("session-a", lateTicket));
        Assert.Null(tickets.Take("session-a", shortCode));
    }

    [Fact]
    public void The_ticket_is_not_the_code_and_an_unknown_or_empty_ticket_gives_nothing()
    {
        var tickets = new AgentPairingTickets(new FakeTimeProvider(Start));
        var ticket = tickets.Issue("session-a", "ABCD234567", Start.AddMinutes(5));

        Assert.DoesNotContain("ABCD234567", ticket, StringComparison.Ordinal);
        Assert.Null(tickets.Take("session-a", "not-a-ticket"));
        Assert.Null(tickets.Take("session-a", string.Empty));
        Assert.Null(tickets.Take("session-a", null));
    }
}

/// <summary>The Web's name-based mask of a recorded run (CONTRACTS §4, A2.5): names decide, references stay, the rest is unchanged.</summary>
public sealed class AgentRunMaskingTests
{
    private const string Sentinel = "sentinel-secret-value-7f3a"; // gitleaks:allow (a test fixture that must look like a secret)

    private static HistoryRecordWriteDto Record(string url, List<KeyValue> headers, List<HeaderLine> responseHeaders, string finalUrl) =>
        new(
            "agent",
            new RequestDefinition("GET", url, null, headers, null, new RequestAuth("bearer", Sentinel, null, null, null, null, null)),
            null,
            null,
            new ExecutionResultDto(
                "completed",
                null,
                null,
                new ResponseDto(200, "OK", "HTTP/1.1", responseHeaders, new ResponseBodyDto("utf8", "{}", 2, false, "application/json")),
                new ExecutionTiming(5, 0, 0, 0, 0),
                finalUrl,
                [new RedirectHop(302, finalUrl)],
                [],
                null));

    [Fact]
    public void Secret_named_headers_are_masked_and_a_plain_header_is_kept()
    {
        var masked = AgentRunMasking.MaskRecord(Record(
            "http://localhost/x",
            [new KeyValue("Authorization", Sentinel), new KeyValue("X-Trace", "abc"), new KeyValue("X-Auth-Token", Sentinel)],
            [],
            "http://localhost/x"));

        Assert.Equal(AgentRunMasking.Mask, masked.Request.Headers![0].Value);
        Assert.Equal("abc", masked.Request.Headers![1].Value);
        Assert.Equal(AgentRunMasking.Mask, masked.Request.Headers![2].Value);
    }

    [Fact]
    public void A_variable_reference_is_kept_because_it_holds_no_value()
    {
        var masked = AgentRunMasking.MaskRecord(Record("http://localhost/x", [new KeyValue("Authorization", "{{apiToken}}")], [], "http://localhost/x"));

        Assert.Equal("{{apiToken}}", masked.Request.Headers![0].Value);
    }

    [Fact]
    public void Query_parameters_named_as_secrets_are_masked_in_the_URL_and_the_final_URL()
    {
        var url = $"http://localhost/x?api_key={Sentinel}&page=2&password={Sentinel}";

        var masked = AgentRunMasking.MaskRecord(Record(url, [], [], url));

        Assert.Equal($"http://localhost/x?api_key={AgentRunMasking.Mask}&page=2&password={AgentRunMasking.Mask}", masked.Request.Url);
        Assert.Equal(masked.Request.Url, masked.Result.FinalUrl);
        Assert.DoesNotContain(Sentinel, masked.Result.Redirects[0].Location, StringComparison.Ordinal);
    }

    [Fact]
    public void A_response_cookie_is_masked_and_the_sentinel_is_in_no_masked_record()
    {
        var masked = AgentRunMasking.MaskRecord(Record(
            "http://localhost/x",
            [new KeyValue("Authorization", Sentinel)],
            [new HeaderLine("Set-Cookie", "session=" + Sentinel), new HeaderLine("Content-Type", "application/json")],
            "http://localhost/x"));

        Assert.Equal(AgentRunMasking.Mask, masked.Result.Response!.Headers[0].Value);
        Assert.Equal("application/json", masked.Result.Response.Headers[1].Value);
        var json = System.Text.Json.JsonSerializer.Serialize(masked);
        Assert.DoesNotContain(Sentinel, json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sentinel_held_as_a_bearer_value_is_masked()
    {
        var masked = AgentRunMasking.MaskRecord(Record("http://localhost/x", [], [], "http://localhost/x"));

        Assert.Equal(AgentRunMasking.Mask, masked.Request.Auth!.BearerToken);
    }
}
