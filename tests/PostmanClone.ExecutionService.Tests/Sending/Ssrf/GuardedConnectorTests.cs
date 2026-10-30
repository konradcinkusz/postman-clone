using System.Net;
using System.Net.Sockets;
using PostmanClone.ExecutionService.Options;
using PostmanClone.ExecutionService.Sending.Ssrf;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Ssrf;

/// <summary>
/// The connection-time check with a scripted resolver and a recording dialer: no real network is used. The property under
/// test is which endpoints are dialled, not whether a socket opens.
/// </summary>
public sealed class GuardedConnectorTests
{
    private const string PublicAddress = "93.184.216.34";
    private const string OtherPublicAddress = "93.184.216.35";

    [Fact]
    public async Task A_public_answer_is_dialled_once_at_its_validated_address()
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer(PublicAddress));
        var dialer = new RecordingDialer();

        await using var stream = await Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("example.com", 80), TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
        Assert.Equal([$"{PublicAddress}:80"], dialer.Dialled.Select(e => e.ToString()));
        Assert.Equal(1, resolver.Calls);
    }

    [Theory]
    [InlineData(PublicAddress, "10.0.0.5")]
    [InlineData("10.0.0.5", PublicAddress)]
    [InlineData(PublicAddress, "127.0.0.1")]
    [InlineData(PublicAddress, "169.254.169.254")]
    [InlineData(PublicAddress, "::ffff:192.168.1.1")]
    public async Task A_mixed_answer_with_any_blocked_address_is_refused_and_nothing_is_dialled(string first, string second)
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer(first, second));
        var dialer = new RecordingDialer();

        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("mixed.example", 443), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("ssrf_blocked_address", rejected.ErrorCode);
        Assert.Empty(dialer.Dialled);
    }

    [Theory]
    [InlineData("127.0.0.1", "loopback")]
    [InlineData("::1", "loopback")]
    [InlineData("::ffff:127.0.0.1", "loopback")]
    [InlineData("0.0.0.0", "this_network")]
    [InlineData("10.20.30.40", "private")]
    [InlineData("172.20.0.1", "private")]
    [InlineData("192.168.10.10", "private")]
    [InlineData("169.254.169.254", "link_local")]
    [InlineData("fe80::1", "link_local")]
    [InlineData("100.64.0.1", "shared_address")]
    [InlineData("fc00::1", "private")]
    [InlineData("224.0.0.1", "multicast")]
    [InlineData("255.255.255.255", "broadcast")]
    [InlineData("2001:db8::1", "documentation")]
    [InlineData("64:ff9b::a9fe:a9fe", "link_local")]
    [InlineData("2002:0a00:0001::", "private")]
    public async Task Each_blocked_answer_is_refused_with_its_category(string address, string category)
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer(address));
        var dialer = new RecordingDialer();

        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("name.example", 80), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("ssrf_blocked_address", rejected.ErrorCode);
        Assert.Equal(category, rejected.Category);
        Assert.Empty(dialer.Dialled);
    }

    [Theory]
    [InlineData("metadata.google.internal", "169.254.169.254")]
    [InlineData("internal.example", "10.1.2.3")]
    [InlineData("loopback.example", "127.0.0.1")]
    public async Task A_name_that_resolves_to_a_blocked_address_is_refused_whatever_the_name_says(string host, string address)
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer(address));
        var dialer = new RecordingDialer();

        await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint(host, 80), TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(dialer.Dialled);
    }

    [Fact]
    public async Task With_AllowPrivateRanges_the_metadata_name_resolves_and_is_dialled()
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer("169.254.169.254"));
        var dialer = new RecordingDialer();

        await Connector(resolver, dialer, new SsrfOptions { AllowPrivateRanges = true })
            .ConnectAsync(new DnsEndPoint("metadata.google.internal", 80), TestContext.Current.CancellationToken);

        Assert.Equal(["169.254.169.254:80"], dialer.Dialled.Select(e => e.ToString()));
    }

    [Fact]
    public async Task A_localhost_name_is_refused_without_the_flag()
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer("127.0.0.1", "::1"));
        var dialer = new RecordingDialer();

        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("localhost", 8080), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("loopback", rejected.Category);
        Assert.Empty(dialer.Dialled);
    }

    [Fact]
    public async Task With_AllowLoopback_localhost_is_dialled_at_the_address_it_resolved_to()
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer("127.0.0.1"));
        var dialer = new RecordingDialer();

        await Connector(resolver, dialer, new SsrfOptions { AllowLoopback = true })
            .ConnectAsync(new DnsEndPoint("localhost", 8080), TestContext.Current.CancellationToken);

        Assert.Equal(["127.0.0.1:8080"], dialer.Dialled.Select(e => e.ToString()));
    }

    [Fact]
    public async Task A_resolver_that_answers_differently_on_the_second_call_is_not_trusted_for_the_first_connection()
    {
        // The first answer is public and the second is loopback: a rebinding resolver. Each connection resolves and checks
        // afresh, so the first connection dials the public address it validated and the second is refused on its own answer.
        var resolver = new ScriptedResolver(
            ScriptedResolver.Answer(PublicAddress),
            ScriptedResolver.Answer("127.0.0.1"));
        var dialer = new RecordingDialer();
        var connector = Connector(resolver, dialer);

        await connector.ConnectAsync(new DnsEndPoint("rebind.example", 80), TestContext.Current.CancellationToken);
        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            connector.ConnectAsync(new DnsEndPoint("rebind.example", 80), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("loopback", rejected.Category);
        Assert.Equal([$"{PublicAddress}:80"], dialer.Dialled.Select(e => e.ToString()));
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public async Task Within_one_connection_a_second_validated_address_is_dialled_after_the_first_fails_and_the_name_is_not_resolved_again()
    {
        var resolver = new ScriptedResolver(
            ScriptedResolver.Answer(PublicAddress, OtherPublicAddress),
            ScriptedResolver.Answer("127.0.0.1"));
        var dialer = new RecordingDialer(endpoint => endpoint.Address.ToString() == PublicAddress);

        await Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("two.example", 80), TestContext.Current.CancellationToken);

        Assert.Equal([$"{PublicAddress}:80", $"{OtherPublicAddress}:80"], dialer.Dialled.Select(e => e.ToString()));
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task When_every_validated_address_fails_the_last_socket_error_is_reported()
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer(PublicAddress, OtherPublicAddress));
        var dialer = new RecordingDialer(_ => true);

        var failure = await Assert.ThrowsAsync<SocketException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("down.example", 80), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(SocketError.ConnectionRefused, failure.SocketErrorCode);
        Assert.Equal(2, dialer.Dialled.Count);
    }

    [Fact]
    public async Task A_resolver_failure_is_reported_as_dns_failed()
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Failure());
        var dialer = new RecordingDialer();

        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("missing.example", 80), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("dns_failed", rejected.ErrorCode);
        Assert.Empty(dialer.Dialled);
    }

    [Fact]
    public async Task An_empty_answer_is_reported_as_dns_failed()
    {
        var resolver = new ScriptedResolver(() => []);
        var dialer = new RecordingDialer();

        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("empty.example", 80), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("dns_failed", rejected.ErrorCode);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(5432)]
    [InlineData(27017)]
    public async Task A_blocked_port_is_refused_before_any_resolution(int port)
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer(PublicAddress));
        var dialer = new RecordingDialer();

        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("example.com", port), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("ssrf_blocked_port", rejected.ErrorCode);
        Assert.Equal(0, resolver.Calls);
        Assert.Empty(dialer.Dialled);
    }

    [Theory]
    [InlineData("2130706433")]
    [InlineData("0x7f.1")]
    [InlineData("0177.0.0.1")]
    [InlineData("::1")]
    [InlineData("169.254.169.254")]
    public async Task A_literal_host_is_judged_as_an_address_without_any_resolution(string host)
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer(PublicAddress));
        var dialer = new RecordingDialer();

        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint(host, 80), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("ssrf_blocked_address", rejected.ErrorCode);
        Assert.Equal(0, resolver.Calls);
        Assert.Empty(dialer.Dialled);
    }

    [Fact]
    public async Task A_public_literal_host_is_dialled_without_any_resolution()
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer("127.0.0.1"));
        var dialer = new RecordingDialer();

        await Connector(resolver, dialer).ConnectAsync(new DnsEndPoint(PublicAddress, 443), TestContext.Current.CancellationToken);

        Assert.Equal(0, resolver.Calls);
        Assert.Equal([$"{PublicAddress}:443"], dialer.Dialled.Select(e => e.ToString()));
    }

    [Fact]
    public async Task A_public_IPv6_literal_host_is_dialled_without_any_resolution()
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer("127.0.0.1"));
        var dialer = new RecordingDialer();

        await Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("2606:4700:4700::1111", 443), TestContext.Current.CancellationToken);

        Assert.Equal(0, resolver.Calls);
        Assert.Equal(["[2606:4700:4700::1111]:443"], dialer.Dialled.Select(e => e.ToString()));
    }

    [Fact]
    public async Task A_host_that_looks_like_an_address_but_is_not_one_is_invalid_and_never_resolved()
    {
        var resolver = new ScriptedResolver(ScriptedResolver.Answer(PublicAddress));
        var dialer = new RecordingDialer();

        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("1.2.3.4.5", 80), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("invalid_url", rejected.ErrorCode);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task A_zone_scoped_answer_is_refused_as_scoped()
    {
        var resolver = new ScriptedResolver(() => [IPAddress.Parse("2606:4700:4700::1111%3")]);
        var dialer = new RecordingDialer();

        var rejected = await Assert.ThrowsAsync<SsrfRejectedException>(() =>
            Connector(resolver, dialer).ConnectAsync(new DnsEndPoint("scoped.example", 443), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("scoped_address", rejected.Category);
        Assert.Empty(dialer.Dialled);
    }

    [Fact]
    public void A_refusal_is_found_through_the_exception_chain_that_the_HTTP_stack_wraps_around_it()
    {
        var refusal = new SsrfRejectedException("ssrf_blocked_address", "private");
        var wrapped = new HttpRequestException("wrapped", new InvalidOperationException("outer", refusal));

        var found = SsrfRejectedException.Find(wrapped);

        Assert.Same(refusal, found);
        Assert.Null(SsrfRejectedException.Find(new HttpRequestException("no refusal here")));
    }

    private static GuardedConnector Connector(IHostResolver resolver, IAddressDialer dialer, SsrfOptions? options = null) =>
        new(options ?? new SsrfOptions(), resolver, dialer);
}
