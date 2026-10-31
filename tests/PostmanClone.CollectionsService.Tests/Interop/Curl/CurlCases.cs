namespace PostmanClone.CollectionsService.Tests.Interop.Curl;

/// <summary>
/// The command shapes the importer must read (WP17 acceptance: at least 40). They follow the look of real Stripe, GitHub
/// and httpbin examples and were written for these tests: no network is used, and every host is <c>.example.invalid</c>.
/// Each case names what it must import. A secret-looking fixture carries <c>gitleaks:allow</c>.
/// </summary>
public static class CurlCases
{
    /// <summary>
    /// What a case must import. <see cref="Auth"/> is <c>none</c>, or <c>basic|user|password</c> with <c>&lt;none&gt;</c>
    /// for an absent password. <see cref="Codes"/> are the warning codes in order.
    /// </summary>
    public sealed record Case(
        string Command,
        string Method,
        string Url,
        string Body = "none",
        string? Raw = null,
        string[]? Headers = null,
        string[]? Codes = null,
        string[]? Fields = null,
        string Auth = "none");

    private const string Api = "https://api.example.invalid";

    public static IReadOnlyDictionary<string, Case> All { get; } = new Dictionary<string, Case>(StringComparer.Ordinal)
    {
        ["get-simple"] = new($"curl {Api}/items", "GET", $"{Api}/items"),
        ["post-json-httpbin"] = new(
            "curl -X POST https://httpbin.example.invalid/post -H \"Content-Type: application/json\" -d '{\"name\":\"x\"}'",
            "POST",
            "https://httpbin.example.invalid/post",
            "raw",
            "{\"name\":\"x\"}",
            ["Content-Type: application/json"]),
        ["long-request-equals"] = new($"curl --request=PUT {Api}/items/1 --data-raw 'x'", "PUT", $"{Api}/items/1", "raw", "x"),
        ["attached-short-value"] = new($"curl -XDELETE {Api}/items/1", "DELETE", $"{Api}/items/1"),
        ["bundled-silent-flags"] = new($"curl -sSL {Api}/redirect", "GET", $"{Api}/redirect"),
        ["head-flag"] = new($"curl -I {Api}/", "HEAD", $"{Api}/"),
        ["get-moves-data-to-query"] = new($"curl -G {Api}/search -d q=hello -d lang=en", "GET", $"{Api}/search?q=hello&lang=en"),
        ["get-appends-to-query"] = new($"curl -G '{Api}/s?a=1' --data-urlencode 'q=a b'", "GET", $"{Api}/s?a=1&q=a%20b"),
        ["data-joined-with-ampersand"] = new($"curl {Api}/form -d a=1 -d b=2", "POST", $"{Api}/form", "raw", "a=1&b=2"),
        ["form-text-fields"] = new(
            $"curl -F name=alice -F 'role=admin' {Api}/upload",
            "POST",
            $"{Api}/upload",
            "formData",
            Fields: ["name=alice", "role=admin"]),
        ["form-file-refused"] = new(
            $"curl -F 'avatar=@/tmp/face.png' -F title=x {Api}/upload",
            "POST",
            $"{Api}/upload",
            "formData",
            Codes: ["file_not_imported"],
            Fields: ["title=x"]),
        ["form-type-suffix-dropped"] = new(
            $"curl -F 'part=hello;type=text/plain' {Api}/upload",
            "POST",
            $"{Api}/upload",
            "formData",
            Fields: ["part=hello"]),
        ["form-string-keeps-at"] = new(
            $"curl --form-string 'at=@not-a-file' {Api}/upload",
            "POST",
            $"{Api}/upload",
            "formData",
            Fields: ["at=@not-a-file"]),
        ["basic-literal-password"] = new( // gitleaks:allow
            $"curl -u alice:hunter2 {Api}/me", // gitleaks:allow
            "GET",
            $"{Api}/me",
            Codes: ["secret_not_imported"],
            Auth: "basic|alice|{{password}}"),
        ["basic-reference-password"] = new(
            $"curl -u 'alice:{{{{password}}}}' {Api}/me",
            "GET",
            $"{Api}/me",
            Auth: "basic|alice|{{password}}"),
        ["basic-key-as-username"] = new( // gitleaks:allow
            $"curl {Api}/v1/charges -u sk_test_example: -d amount=2000 -d currency=usd", // gitleaks:allow
            "POST",
            $"{Api}/v1/charges",
            "raw",
            "amount=2000&currency=usd",
            Auth: "basic|sk_test_example|"),
        ["github-variable-bearer"] = new(
            $"curl -L -H \"Accept: application/vnd.github+json\" -H \"Authorization: Bearer {{{{github_token}}}}\" -H \"X-GitHub-Api-Version: 2022-11-28\" {Api}/repos/o/r/issues",
            "GET",
            $"{Api}/repos/o/r/issues",
            Headers: ["Accept: application/vnd.github+json", "Authorization: Bearer {{github_token}}", "X-GitHub-Api-Version: 2022-11-28"]),
        ["github-literal-bearer"] = new( // gitleaks:allow
            $"curl -L -H \"Authorization: Bearer ghp_example_literal\" {Api}/repos/o/r/issues", // gitleaks:allow
            "GET",
            $"{Api}/repos/o/r/issues",
            Headers: ["Authorization: Bearer ghp_example_literal"],
            Codes: ["sensitive_value_kept"]),
        ["cookie-string"] = new(
            $"curl -b 'session=abc; theme=dark' {Api}/",
            "GET",
            $"{Api}/",
            Headers: ["Cookie: session=abc; theme=dark"],
            Codes: ["sensitive_value_kept"]),
        ["cookie-file-refused"] = new($"curl -b cookies.txt {Api}/", "GET", $"{Api}/", Codes: ["file_not_imported"]),
        ["header-without-colon"] = new($"curl -H 'X-Broken' {Api}/", "GET", $"{Api}/", Codes: ["invalid_argument"]),
        ["header-bare-semicolon"] = new($"curl -H 'X-Empty;' {Api}/", "GET", $"{Api}/", Headers: ["X-Empty: "]),
        ["header-empty-removes"] = new($"curl -H 'X-Remove:' {Api}/", "GET", $"{Api}/", Codes: ["invalid_argument"]),
        ["header-line-break-refused"] = new($"curl -H $'X-A: 1\\nX-B: 2' {Api}/", "GET", $"{Api}/", Codes: ["invalid_argument"]),
        ["single-quotes-literal"] = new($"curl {Api}/q -H 'X-Raw: $HOME \\n'", "GET", $"{Api}/q", Headers: ["X-Raw: $HOME \\n"]),
        ["double-quotes-escapes"] = new(
            $"curl {Api}/q -H \"X-Quote: say \\\"hi\\\" \\\\ done\"",
            "GET",
            $"{Api}/q",
            Headers: ["X-Quote: say \"hi\" \\ done"]),
        ["double-quote-expansion-kept"] = new(
            $"curl {Api}/q -H \"X-Trace: $TRACE_ID\"",
            "GET",
            $"{Api}/q",
            Headers: ["X-Trace: $TRACE_ID"],
            Codes: ["shell_expansion_kept"]),
        ["command-substitution-kept"] = new(
            $"curl {Api}/q -H \"X-Cmd: $(whoami)\"",
            "GET",
            $"{Api}/q",
            Headers: ["X-Cmd: $(whoami)"],
            Codes: ["shell_expansion_kept"]),
        ["backticks-single-quoted"] = new($"curl {Api}/q -H 'X-Tick: `id`'", "GET", $"{Api}/q", Headers: ["X-Tick: `id`"]),
        ["line-continuation-lf"] = new(
            $"curl {Api}/c \\\n  -X PATCH \\\n  -d '{{}}'",
            "PATCH",
            $"{Api}/c",
            "raw",
            "{}"),
        ["line-continuation-crlf"] = new($"curl {Api}/c \\\r\n  -X PUT", "PUT", $"{Api}/c"),
        ["continuation-inside-double-quotes"] = new(
            $"curl {Api}/a -H \"X-Long: a\\\nb\"",
            "GET",
            $"{Api}/a",
            Headers: ["X-Long: ab"]),
        ["ansi-escapes-tab"] = new($"curl {Api}/a -H $'X-Tab: a\\tb'", "GET", $"{Api}/a", Headers: ["X-Tab: a\tb"]),
        ["ansi-unicode-and-quote"] = new($"curl {Api}/a -d $'caf\\u00e9 it\\'s'", "POST", $"{Api}/a", "raw", "café it's"),
        ["data-raw-keeps-at"] = new($"curl --data-raw '@not-a-file' {Api}/p", "POST", $"{Api}/p", "raw", "@not-a-file"),
        ["data-file-refused"] = new($"curl -d @payload.json {Api}/p", "POST", $"{Api}/p", "raw", string.Empty, Codes: ["file_not_imported"]),
        ["urlencode-name-and-bare"] = new(
            $"curl {Api}/f --data-urlencode 'q=a&b' --data-urlencode '=x y'",
            "POST",
            $"{Api}/f",
            "raw",
            "q=a%26b&x%20y"),
        ["data-and-form-both"] = new(
            $"curl -d a=1 -F b=2 {Api}/x",
            "POST",
            $"{Api}/x",
            "formData",
            Codes: ["unsupported_body"],
            Fields: ["b=2"]),
        ["lowercase-method-and-body"] = new($"curl -X get {Api}/x -d x=1", "GET", $"{Api}/x", "raw", "x=1"),
        ["user-agent-ignored"] = new($"curl -A 'agent/1.0' {Api}/a", "GET", $"{Api}/a", Codes: ["option_ignored"]),
        ["compressed-silent"] = new($"curl --compressed {Api}/a", "GET", $"{Api}/a"),
        ["insecure-warned"] = new("curl -k https://self-signed.example.invalid/", "GET", "https://self-signed.example.invalid/", Codes: ["option_ignored"]),
        ["unknown-flag"] = new($"curl --frobnicate {Api}/a", "GET", $"{Api}/a", Codes: ["unsupported_option"]),
        ["extra-url-ignored"] = new(
            "curl https://a.example.invalid https://b.example.invalid",
            "GET",
            "https://a.example.invalid",
            Codes: ["extra_argument_ignored"]),
        ["url-flag-and-method"] = new($"curl --url {Api}/u -X DELETE", "DELETE", $"{Api}/u"),
        ["cmd-caret"] = new(
            $"curl.exe -X POST {Api}/a -H \"Accept: application/json\" ^",
            "POST",
            $"{Api}/a",
            Headers: ["Accept: application/json"],
            Codes: ["cmd_syntax_unsupported", "extra_argument_ignored"]),
        ["newline-separated-option"] = new($"curl {Api}/a\n-X POST", "POST", $"{Api}/a"),
        ["option-missing-value"] = new($"curl {Api}/a -X", "GET", $"{Api}/a", Codes: ["invalid_argument"]),
        ["basic-no-password"] = new($"curl -u alice {Api}/a", "GET", $"{Api}/a", Auth: "basic|alice|<none>"),
        ["url-userinfo-password"] = new( // gitleaks:allow
            "curl https://alice:hunter2@api.example.invalid/a", // gitleaks:allow
            "GET",
            "https://alice:{{password}}@api.example.invalid/a",
            Codes: ["secret_not_imported"]),
        ["query-credential-kept"] = new( // gitleaks:allow
            $"curl '{Api}/x?api_key=abc123&page=2'",
            "GET",
            $"{Api}/x?api_key=abc123&page=2",
            Codes: ["sensitive_value_kept"]),
        ["end-of-options"] = new($"curl -- {Api}/a", "GET", $"{Api}/a"),
        ["header-value-trimmed"] = new($"curl -H \"Accept:   text/plain  \" {Api}/a", "GET", $"{Api}/a", Headers: ["Accept: text/plain"]),
        ["shell-variable-in-url-kept"] = new(
            $"curl {Api}/$VERSION/items",
            "GET",
            $"{Api}/$VERSION/items",
            Codes: ["shell_expansion_kept"]),
    };
}
