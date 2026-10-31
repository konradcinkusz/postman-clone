namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// What the journeys read from the environment. Every value has a default that matches the local stack
/// (scripts/run-local-stack.sh, README §3). Nothing here is a credential.
/// </summary>
public static class E2ESettings
{
    /// <summary>The Web the browser opens (launchSettings: 5310).</summary>
    public static Uri WebBaseUrl { get; } = new(Environment.GetEnvironmentVariable("E2E_WEB_URL") ?? "http://localhost:5310");

    /// <summary>authservice, called directly to create a user and an organization (the same calls the Web makes).</summary>
    public static Uri AuthBaseUrl { get; } = new(Environment.GetEnvironmentVariable("E2E_AUTH_URL") ?? "http://localhost:5300");

    /// <summary>CollectionsService, called directly to set up data for isolation checks.</summary>
    public static Uri CollectionsBaseUrl { get; } = new(Environment.GetEnvironmentVariable("E2E_COLLECTIONS_URL") ?? "http://localhost:5301");

    /// <summary>
    /// When "1", a missing prerequisite (no stack, no browser) FAILS the run instead of skipping the journeys. CI sets it, so
    /// a broken job cannot pass by skipping. Locally it is unset, and a missing prerequisite skips with the reason.
    /// </summary>
    public static bool Required => Environment.GetEnvironmentVariable("POSTMANCLONE_E2E_REQUIRED") == "1";

    /// <summary>An explicit Chromium executable. Empty means the search in BrowserLocator.</summary>
    public static string? ChromiumPath => Environment.GetEnvironmentVariable("E2E_CHROMIUM_PATH");

    /// <summary>
    /// The sender's loopback setting the stack was started with (scripts/run-local-stack.sh, E2E_SENDER_LOOPBACK=1).
    /// The journeys tagged "loopback" need it; the journeys tagged "strict" need it absent. The trait selects which
    /// job runs which journey, so a wrong filter is visible as a failing journey, not a skip.
    /// </summary>
    public const string LoopbackTrait = "loopback";
    public const string StrictTrait = "strict";
}
