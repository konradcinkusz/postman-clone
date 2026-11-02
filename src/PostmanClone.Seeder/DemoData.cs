using System.Text.Json.Nodes;

namespace PostmanClone.Seeder;

/// <summary>
/// The whole demo dataset in one place (ADR-0030). Every name that the seeder creates and the reset removes sits under
/// <see cref="Prefix"/>; the two accounts sit under <see cref="EmailDomain"/>. Nothing is random: two runs give the same data.
/// </summary>
internal static class DemoData
{
    /// <summary>The reserved name namespace. Reset deletes only collections and environments whose name starts with it.</summary>
    public const string Prefix = "[demo] ";

    public const string EmailDomain = "postmanclone.test";
    public const string OwnerEmail = "demo.owner@" + EmailDomain;
    public const string MemberEmail = "demo.member@" + EmailDomain;

    /// <summary>The published development password of both demo accounts. It meets authservice's rule (docs/AUTH.md).</summary>
    public const string DefaultPassword = "Demo-Passw0rd!"; // gitleaks:allow — a published local demo value, not a credential.

    public const string OrganizationName = "Demo Workspace";

    public const string CollectionName = Prefix + "Getting started";
    public const string FolderName = "Echo";
    public const string EnvironmentName = Prefix + "Local demo";

    /// <summary>The variable that is plain and the one that is secret. The secret's value is obviously fake.</summary>
    public const string BaseUrlKey = "base_url";
    public const string BaseUrlValue = "https://httpbin.org";
    public const string SecretKey = "api_key";
    public const string SecretValue = "demo-not-a-real-key"; // gitleaks:allow — an obviously fake value for the demo secret variable.

    public const string GetRequestName = "Get example.com";
    public const string GetRequestUrl = "https://example.com/";
    public const string PostRequestName = "Post a JSON note";

    public static bool IsDemoName(string? name) => name is not null && name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>A GET to a public URL, with no variable in it, so it sends without an environment.</summary>
    public static JsonObject GetDefinition() => Definition("GET", GetRequestUrl, headers: [], body: None());

    /// <summary>A POST with a JSON body, using the plain variable and the secret variable (as a header, which is masked in history).</summary>
    public static JsonObject PostDefinition() => Definition(
        "POST",
        "{{" + BaseUrlKey + "}}/post",
        headers:
        [
            Row("Content-Type", "application/json"),
            Row("X-Demo-Key", "{{" + SecretKey + "}}"),
        ],
        body: new JsonObject
        {
            ["type"] = "raw",
            ["rawLanguage"] = "json",
            ["raw"] = "{\n  \"note\": \"Hello from the demo workspace\",\n  \"demo\": true\n}",
            ["fields"] = new JsonArray(),
        });

    public static JsonArray EnvironmentVariables(bool keepExistingSecret) =>
    [
        new JsonObject { ["key"] = BaseUrlKey, ["value"] = BaseUrlValue, ["isSecret"] = false, ["enabled"] = true, ["keepExistingSecret"] = false },
        new JsonObject
        {
            ["key"] = SecretKey,
            ["value"] = keepExistingSecret ? null : SecretValue,
            ["isSecret"] = true,
            ["enabled"] = true,
            ["keepExistingSecret"] = keepExistingSecret,
        },
    ];

    private static JsonObject None() => new() { ["type"] = "none", ["rawLanguage"] = null, ["raw"] = null, ["fields"] = new JsonArray() };

    private static JsonObject Row(string key, string value) => new() { ["key"] = key, ["value"] = value, ["enabled"] = true };

    private static JsonObject Definition(string method, string url, JsonObject[] headers, JsonObject body) => new()
    {
        ["method"] = method,
        ["url"] = url,
        ["params"] = new JsonArray(),
        ["headers"] = new JsonArray(headers.Select(h => (JsonNode)h).ToArray()),
        ["body"] = body,
        ["auth"] = new JsonObject { ["type"] = "none" },
    };
}
