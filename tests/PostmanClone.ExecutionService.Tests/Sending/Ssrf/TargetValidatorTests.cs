using PostmanClone.ExecutionService.Tests.Support;
using PostmanClone.ExecutionService.Options;
using PostmanClone.Sending.Ssrf;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Ssrf;

/// <summary>The URL checks before anything is sent: scheme, syntax, port, literal hosts and their odd spellings.</summary>
public sealed class TargetValidatorTests
{
    [Theory]
    [InlineData("", "invalid_url")]
    [InlineData("   ", "invalid_url")]
    [InlineData("not a url", "invalid_url")]
    [InlineData("http://", "invalid_url")]
    [InlineData("http:127.0.0.1", "invalid_url")]
    [InlineData("1http://example.com/", "invalid_url")]
    [InlineData("http://user@example.com/", "invalid_url")]
    [InlineData("http://user:x@example.com/", "invalid_url")]
    [InlineData("http://allowed.example@127.0.0.1/", "invalid_url")]
    [InlineData("http://example.com:0/", "invalid_url")]
    [InlineData("http://example.com:65536/", "invalid_url")]
    [InlineData("http://example.com:99999/", "invalid_url")]
    [InlineData("http://example.com:abc/", "invalid_url")]
    [InlineData("http://example.com:80:81/", "invalid_url")]
    [InlineData("http://[::1/", "invalid_url")]
    [InlineData("http://[::1]x/", "invalid_url")]
    [InlineData("http://exa mple.com/", "invalid_url")]
    [InlineData("http://a..b/", "invalid_url")]
    [InlineData("http://.example/", "invalid_url")]
    [InlineData("http://./", "invalid_url")]
    [InlineData("http://bücher\u0000.example/", "invalid_url")]
    [InlineData("http://0x1g.0.0.1/", "invalid_url")]
    [InlineData("http://256.0.0.1/", "invalid_url")]
    [InlineData("ftp://example.com/", "ssrf_blocked_scheme")]
    [InlineData("gopher://example.com/", "ssrf_blocked_scheme")]
    [InlineData("file:///etc/passwd", "ssrf_blocked_scheme")]
    [InlineData("javascript:alert(1)", "ssrf_blocked_scheme")]
    [InlineData("data:text/plain,hi", "ssrf_blocked_scheme")]
    [InlineData("ws://example.com/", "ssrf_blocked_scheme")]
    [InlineData("dict://example.com:11211/", "ssrf_blocked_scheme")]
    [InlineData("http://example.com:22/", "ssrf_blocked_port")]
    [InlineData("https://example.com:25/", "ssrf_blocked_port")]
    [InlineData("http://example.com:110/", "ssrf_blocked_port")]
    [InlineData("http://example.com:143/", "ssrf_blocked_port")]
    [InlineData("http://example.com:465/", "ssrf_blocked_port")]
    [InlineData("http://example.com:587/", "ssrf_blocked_port")]
    [InlineData("http://example.com:993/", "ssrf_blocked_port")]
    [InlineData("http://example.com:995/", "ssrf_blocked_port")]
    [InlineData("http://example.com:3306/", "ssrf_blocked_port")]
    [InlineData("http://example.com:5432/", "ssrf_blocked_port")]
    [InlineData("http://example.com:6379/", "ssrf_blocked_port")]
    [InlineData("http://example.com:9200/", "ssrf_blocked_port")]
    [InlineData("http://example.com:27017/", "ssrf_blocked_port")]
    [InlineData("http://127.0.0.1:22/", "ssrf_blocked_port")]
    [InlineData("http://[::1]:5432/", "ssrf_blocked_port")]
    public void A_malformed_or_disallowed_URL_is_refused_with_its_code(string url, string errorCode)
    {
        var check = Validate(url);

        Assert.False(check.Accepted);
        Assert.Equal(errorCode, check.ErrorCode);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://0177.0.0.1/")]
    [InlineData("http://0x7f.1/")]
    [InlineData("http://0x7f000001/")]
    [InlineData("http://017700000001/")]
    [InlineData("http://127.1/")]
    [InlineData("http://127.0.0.1./")]
    [InlineData("http://0x7f.0.0.0x1/")]
    [InlineData("http://%31%32%37.0.0.1/")]
    [InlineData("http://127.0.0.1:8080/path")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[0:0:0:0:0:0:0:1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[::ffff:7f00:1]/")]
    [InlineData("ht\ttp://127.0.0.1/")]
    [InlineData("http://127.0\n.0.1/")]
    [InlineData("  http://127.0.0.1/  ")]
    [InlineData("http:\\\\127.0.0.1\\")]
    [InlineData("http://127.0.0.1\\@example.com/")]
    public void A_loopback_address_in_any_spelling_is_refused_as_loopback(string url)
    {
        var check = Validate(url);

        Assert.False(check.Accepted);
        Assert.Equal("ssrf_blocked_address", check.ErrorCode);
        Assert.Equal("loopback", check.Category);
    }

    [Theory]
    [InlineData("http://0/", "this_network")]
    [InlineData("http://0.0.0.0/", "this_network")]
    [InlineData("http://0x0/", "this_network")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "link_local")]
    [InlineData("http://0251.0376.0251.0376/", "link_local")]
    [InlineData("http://0xa9.0xfe.0xa9.0xfe/", "link_local")]
    [InlineData("http://[fe80::1]/", "link_local")]
    [InlineData("http://[64:ff9b::a9fe:a9fe]/", "link_local")]
    [InlineData("http://10.0.0.1/", "private")]
    [InlineData("http://192.168.0.1/", "private")]
    [InlineData("http://172.16.0.1:8080/", "private")]
    [InlineData("http://[fc00::1]/", "private")]
    [InlineData("http://100.64.0.1/", "shared_address")]
    [InlineData("http://224.0.0.1/", "multicast")]
    [InlineData("http://255.255.255.255/", "broadcast")]
    [InlineData("http://[2001:db8::1]/", "documentation")]
    public void A_blocked_literal_address_in_any_spelling_is_refused_with_its_category(string url, string category)
    {
        var check = Validate(url);

        Assert.False(check.Accepted);
        Assert.Equal("ssrf_blocked_address", check.ErrorCode);
        Assert.Equal(category, check.Category);
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST/")]
    [InlineData("http://localhost./")]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://api.localhost/")]
    [InlineData("https://a.b.localhost/x")]
    public void A_localhost_name_is_refused_as_loopback_without_the_flag(string url)
    {
        var check = Validate(url);

        Assert.False(check.Accepted);
        Assert.Equal("ssrf_blocked_address", check.ErrorCode);
        Assert.Equal("loopback", check.Category);
    }

    [Theory]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("HTTP://EXAMPLE.COM/Path", "http://example.com/Path")]
    [InlineData("https://example.com:443/a?x=1#frag", "https://example.com/a?x=1")]
    [InlineData("http://example.com:80/", "http://example.com/")]
    [InlineData("http://example.com:8080/a", "http://example.com:8080/a")]
    [InlineData("https://example.com:80/", "https://example.com:80/")]
    [InlineData("http://example.com./", "http://example.com/")]
    [InlineData("http://example.com:1/", "http://example.com:1/")]
    [InlineData("https://example.com:3389/", "https://example.com:3389/")]
    [InlineData("http://example.com:65535/", "http://example.com:65535/")]
    [InlineData("http://bücher.example/", "http://xn--bcher-kva.example/")]
    [InlineData("http://example.com\\@127.0.0.1/", "http://example.com/@127.0.0.1/")]
    [InlineData("http://example.com/a\\b", "http://example.com/a/b")]
    [InlineData("http://example.com/?q=a\\b", "http://example.com/?q=a\\b")]
    [InlineData("http://metadata.google.internal/", "http://metadata.google.internal/")]
    [InlineData("http://exa_mple.example/", "http://exa_mple.example/")]
    [InlineData("http://0x5db8d822/", "http://93.184.216.34/")]
    [InlineData("http://93.184.216.34/", "http://93.184.216.34/")]
    [InlineData("http://[2606:4700:4700::1111]/", "http://[2606:4700:4700::1111]/")]
    public void A_public_target_is_accepted_and_returned_in_its_canonical_form(string url, string canonical)
    {
        var check = Validate(url);

        Assert.True(check.Accepted, check.ErrorCode);
        Assert.Equal(canonical, check.Target!.Uri);
    }

    [Fact]
    public void A_name_is_not_resolved_here_so_it_is_accepted_and_left_to_the_connection_check()
    {
        var check = Validate("https://example.com/");

        Assert.True(check.Accepted);
        Assert.False(check.Target!.IsLiteral);
        Assert.Equal("example.com", check.Target.Host);
    }

    [Fact]
    public void A_literal_host_is_reported_as_a_literal_with_its_address_as_host()
    {
        var check = Validate("http://0x5db8d822:8080/");

        Assert.True(check.Accepted);
        Assert.True(check.Target!.IsLiteral);
        Assert.Equal("93.184.216.34", check.Target.Host);
        Assert.Equal(8080, check.Target.Port);
    }

    [Theory]
    [InlineData("http://localhost/", "http://localhost/")]
    [InlineData("http://127.0.0.1:5999/", "http://127.0.0.1:5999/")]
    [InlineData("http://2130706433/", "http://127.0.0.1/")]
    [InlineData("http://0x7f.1/", "http://127.0.0.1/")]
    [InlineData("http://[::1]/", "http://[::1]/")]
    public void AllowLoopback_accepts_loopback_in_any_spelling_and_canonicalises_it(string url, string canonical)
    {
        var check = Validate(url, new ExecutionOptions { Ssrf = new SsrfOptions { AllowLoopback = true } });

        Assert.True(check.Accepted, check.ErrorCode);
        Assert.Equal(canonical, check.Target!.Uri);
    }

    [Theory]
    [InlineData("http://10.1.2.3/", true)]
    [InlineData("http://169.254.169.254/", true)]
    [InlineData("http://100.64.0.1/", false)]
    [InlineData("http://127.0.0.1/", false)]
    public void AllowPrivateRanges_accepts_private_addresses_only(string url, bool accepted)
    {
        var check = Validate(url, new ExecutionOptions { Ssrf = new SsrfOptions { AllowPrivateRanges = true } });

        Assert.Equal(accepted, check.Accepted);
    }

    [Fact]
    public void A_URL_longer_than_the_configured_limit_is_refused()
    {
        var url = "https://example.com/" + new string('a', 8200);

        var check = Validate(url);

        Assert.Equal("invalid_url", check.ErrorCode);
    }

    [Fact]
    public void A_label_longer_than_63_characters_is_refused()
    {
        var url = "http://" + new string('a', 64) + ".example/";

        var check = Validate(url);

        Assert.Equal("invalid_url", check.ErrorCode);
    }

    [Fact]
    public void A_fragment_is_never_sent()
    {
        var check = Validate("http://example.com/a#secret-part");

        Assert.Equal("http://example.com/a", check.Target!.Uri);
    }

    private static TargetCheck Validate(string url, ExecutionOptions? options = null) =>
        Senders.Validator(options ?? new ExecutionOptions()).Validate(url);
}
