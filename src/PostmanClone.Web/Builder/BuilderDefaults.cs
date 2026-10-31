using PostmanClone.Web.Api;

namespace PostmanClone.Web.Builder;

/// <summary>What a new request holds (WP19): a GET with no rows, no body and no auth. The URL is a bare scheme, so it passes the rules and sends nothing.</summary>
public static class BuilderDefaults
{
    public static RequestDefinition NewDefinition() => new(
        "GET",
        BuilderMessages.NewRequestUrl,
        [],
        [],
        new RequestBody("none", null, null, []),
        new RequestAuth("none", null, null, null, null, null, null));
}
