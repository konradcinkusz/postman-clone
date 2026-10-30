using PostmanClone.ExecutionService.Sending.Assembly;
using Xunit;

namespace PostmanClone.ExecutionService.Tests.Sending.Assembly;

/// <summary>The URL and its query: the URL's own query is kept, pairs are encoded, the fragment is dropped.</summary>
public sealed class UrlBuilderTests
{
    private const int Limit = 8192;

    [Theory]
    [InlineData("https://h.test/p", "", "https://h.test/p")]
    [InlineData("https://h.test/p", "a=1", "https://h.test/p?a=1")]
    [InlineData("https://h.test/p?x=9", "a=1", "https://h.test/p?x=9&a=1")]
    [InlineData("https://h.test/p?x=9", "", "https://h.test/p?x=9")]
    [InlineData("https://h.test/p?", "a=1", "https://h.test/p?a=1")]
    [InlineData("https://h.test/p?", "", "https://h.test/p")]
    [InlineData("https://h.test/p#frag", "a=1", "https://h.test/p?a=1")]
    [InlineData("https://h.test/p?x=1#frag", "", "https://h.test/p?x=1")]
    [InlineData("https://h.test/p?a=1", "a=2&a=3", "https://h.test/p?a=1&a=2&a=3")]
    [InlineData("https://h.test/p", "a=", "https://h.test/p?a=")]
    [InlineData("https://h.test/p", "k=b c", "https://h.test/p?k=b%20c")]
    [InlineData("https://h.test/p", "k=a+b", "https://h.test/p?k=a%2Bb")]
    [InlineData("https://h.test/p", "k=é", "https://h.test/p?k=%C3%A9")]
    [InlineData("https://h.test/p", "k=🙂", "https://h.test/p?k=%F0%9F%99%82")]
    [InlineData("https://h.test/p", "k=~-._", "https://h.test/p?k=~-._")]
    [InlineData("https://h.test/p", "k=%41", "https://h.test/p?k=%2541")]
    [InlineData("https://h.test/p", "a=1&b=2", "https://h.test/p?a=1&b=2")]
    public void Appends_the_pairs_after_the_urls_own_query(string url, string pairs, string expected)
    {
        var query = Pairs(pairs);

        Assert.Equal(expected, UrlBuilder.Build(url, query, Limit));
    }

    [Fact]
    public void Reserved_characters_in_a_name_or_value_are_percent_encoded_so_they_cannot_add_a_pair()
    {
        var query = new[] { new KeyValuePair<string, string>("a&b", "c=d#e?f") };

        Assert.Equal("https://h.test/p?a%26b=c%3Dd%23e%3Ff", UrlBuilder.Build("https://h.test/p", query, Limit));
    }

    [Fact]
    public void A_fragment_inside_an_expanded_value_drops_the_rest_of_the_url()
    {
        Assert.Equal("https://h.test/p", UrlBuilder.Build("https://h.test/p#x?y=1", [], Limit));
    }

    [Fact]
    public void A_url_of_exactly_the_limit_is_accepted()
    {
        var url = "https://h.test/" + new string('a', Limit - "https://h.test/".Length);

        Assert.Equal(Limit, UrlBuilder.Build(url, [], Limit).Length);
    }

    [Fact]
    public void A_url_one_character_over_the_limit_is_refused_with_url_too_long()
    {
        var url = "https://h.test/" + new string('a', Limit - "https://h.test/".Length + 1);

        var refused = AssemblyFixtures.Refused(() => UrlBuilder.Build(url, [], Limit));

        Assert.Equal(RequestAssemblyException.UrlTooLong, refused.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/relative/path")]
    [InlineData("#only-a-fragment")]
    [InlineData("https://h.test/\r\nX-Injected: 1")]
    [InlineData("https://h.test/\n")]
    [InlineData("https://h.test/\u0000")]
    public void A_url_that_is_empty_relative_or_carries_a_control_character_is_refused_with_invalid_url(string url)
    {
        var refused = AssemblyFixtures.Refused(() => UrlBuilder.Build(url, [], Limit));

        Assert.Equal(RequestAssemblyException.InvalidUrl, refused.Code);
    }

    /// <summary>Test input: "name=value" pairs separated by '&amp;', split at the first '='.</summary>
    private static List<KeyValuePair<string, string>> Pairs(string spec) =>
        spec.Length == 0
            ? []
            : spec.Split('&').Select(pair =>
            {
                var equals = pair.IndexOf('=');
                return new KeyValuePair<string, string>(pair[..equals], pair[(equals + 1)..]);
            }).ToList();
}
