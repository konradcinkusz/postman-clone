namespace PostmanClone.Web.Builder;

/// <summary>The options the editor's selects offer, with their labels. The values are the service's (CONTRACTS §1).</summary>
public static class BuilderChoices
{
    public static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public static readonly (string Value, string Label)[] BodyTypes =
    [
        ("none", "None"),
        ("raw", "Raw"),
        ("formUrlEncoded", "Form URL-encoded"),
        ("formData", "Form data (text fields)"),
    ];

    public static readonly string[] RawLanguages = ["json", "text", "xml", "html", "javascript"];

    public static readonly (string Value, string Label)[] AuthTypes =
    [
        ("none", "No auth"),
        ("bearer", "Bearer token"),
        ("basic", "Basic"),
        ("apiKey", "API key"),
    ];

    public static readonly (string Value, string Label)[] ApiKeyLocations =
    [
        ("header", "Header"),
        ("query", "Query parameter"),
    ];
}
