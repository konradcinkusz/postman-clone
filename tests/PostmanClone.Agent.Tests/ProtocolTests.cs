using PostmanClone.Agent.Local;
using Xunit;

namespace PostmanClone.Agent.Tests;

/// <summary>The rules of the local listener as pure functions (CONTRACTS A2.6). The live refusals are in <see cref="ListenerSecurityTests"/>.</summary>
public sealed class ProtocolTests
{
    [Theory]
    [InlineData("127.0.0.1:47631", true)]
    [InlineData("localhost:47631", true)]
    [InlineData("LOCALHOST:47631", true)]
    [InlineData("evil.example:47631", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("127.0.0.1:8080", false)]
    [InlineData("localhost.evil.example:47631", false)]
    [InlineData("[::1]:47631", false)]
    [InlineData("", false)]
    public void Only_the_loopback_names_with_the_agent_port_are_a_host(string host, bool allowed) =>
        Assert.Equal(allowed, LocalProtocol.HostAllowed(host, LocalProtocol.DefaultPort));

    [Fact]
    public void A_missing_host_is_refused() =>
        Assert.False(LocalProtocol.HostAllowed(null, LocalProtocol.DefaultPort));

    [Theory]
    [InlineData("https://app.example", true)]
    [InlineData("https://APP.example", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("null", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("https://app.example.evil.example", false)]
    [InlineData("https://app.example:8443", false)]
    [InlineData("http://app.example", false)]
    [InlineData("https://app.example/", false)]
    public void Only_an_exact_allowed_origin_is_an_origin(string? origin, bool allowed) =>
        Assert.Equal(allowed, LocalProtocol.OriginAllowed(origin, ["https://app.example"]));

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("application/json; charset=utf-8", true)]
    [InlineData("Application/JSON", true)]
    [InlineData("text/plain", false)]
    [InlineData("application/x-www-form-urlencoded", false)]
    [InlineData("application/jsonx", false)]
    [InlineData(null, false)]
    public void Only_json_is_a_body_the_protocol_reads(string? contentType, bool json) =>
        Assert.Equal(json, LocalProtocol.IsJson(contentType));

    [Theory]
    [InlineData("ABCDEFGHJK", true)]
    [InlineData("ABCDEFGHJKMNPQRSTVWX", true)]
    [InlineData("SHORT", false)]
    [InlineData("ABCDEFGHJKMNPQRSTVWXY", false)]
    public void The_pairing_code_has_the_contract_length(string code, bool valid) =>
        Assert.Equal(valid, LocalProtocol.PairingCodeShapeValid(code));
}
