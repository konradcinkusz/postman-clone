using System.Text.RegularExpressions;
using PostmanClone.TestKit;

namespace PostmanClone.Web.Tests.Styles;

/// <summary>
/// The visual design is a frozen token contract (BLAZOR-WEB-APP §9, docs/architecture/UI-STYLE.md). These tests read
/// the stylesheets from disk: both themes define every colour token, the shared class names exist, the focus ring and
/// the reduced-motion rule are there, and no colour appears outside the token blocks.
/// </summary>
public sealed class StyleContractTests
{
    /// <summary>Every colour token. Each must be defined in the light block and redefined in the dark block.</summary>
    private static readonly string[] ColourTokens =
    [
        "surface-page", "surface", "surface-sunken",
        "text", "text-secondary", "text-muted",
        "accent", "accent-hover", "accent-contrast", "accent-soft",
        "success", "success-soft", "warning", "warning-soft", "danger", "danger-soft",
        "border", "shadow",
        "method-get", "method-post", "method-put", "method-patch", "method-delete", "method-head", "method-options",
        "code-key", "code-string", "code-number", "code-keyword", "code-muted",
    ];

    /// <summary>Tokens that are not colours. They live in the light block only, because they do not change with the theme.</summary>
    private static readonly string[] LayoutTokens =
    [
        "radius", "radius-sm", "space-1", "space-2", "space-3", "space-4", "space-5", "space-6", "gutter", "content-width",
        "leading", "font-sans", "font-mono", "text-xs", "text-sm", "text-base", "text-lg", "text-xl", "text-2xl",
    ];

    /// <summary>The shared class names of the contract. A polish task uses these names or asks; it does not invent new ones.</summary>
    private static readonly string[] SharedClasses =
    [
        "card", "stack", "cluster", "field", "btn", "btn-primary", "btn-danger", "badge", "callout", "table-wrap",
        "num", "empty-state", "kv-table", "tabs", "tab", "tree", "tree-node", "split",
    ];

    private static readonly Regex LightBlock = new(@"(?<!@media[^{]*)(?<![\w-]):root\s*\{(?<body>[^}]*)\}");
    private static readonly Regex DarkBlock = new(@"@media \(prefers-color-scheme: dark\)\s*\{\s*:root\s*\{(?<body>[^}]*)\}\s*\}");
    private static readonly Regex TokenBlocks = new(@"@media \(prefers-color-scheme: dark\)\s*\{\s*:root\s*\{[^}]*\}\s*\}|:root\s*\{[^}]*\}");
    private static readonly Regex Comments = new(@"/\*.*?\*/", RegexOptions.Singleline);
    private static readonly Regex Hex = new(@"#[0-9a-fA-F]{3,8}\b");
    private static readonly Regex ColourFunction = new(@"\b(rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\(", RegexOptions.IgnoreCase);
    private static readonly Regex NamedColour = new(
        @"(?<![\w-])(black|white|red|green|blue|gray|grey|silver|maroon|purple|fuchsia|lime|olive|yellow|navy|teal|aqua|orange|transparent|currentcolor)(?![\w-])",
        RegexOptions.IgnoreCase);

    private static readonly string WebRoot = Path.Combine(RepoRoot.Find(), "src", "PostmanClone.Web");
    private static readonly string Stylesheet = File.ReadAllText(Path.Combine(WebRoot, "wwwroot", "app.css"));

    [Fact]
    public void The_light_theme_defines_every_colour_token_and_the_layout_tokens()
    {
        var light = LightBlock.Match(Stylesheet).Groups["body"].Value;

        Assert.Equal(Array.Empty<string>(), ColourTokens.Where(token => !Declares(light, token)));
        Assert.Equal(Array.Empty<string>(), LayoutTokens.Where(token => !Declares(light, token)));
    }

    [Fact]
    public void The_dark_theme_redefines_every_colour_token()
    {
        var dark = DarkBlock.Match(Stylesheet);

        Assert.True(dark.Success, "the dark block (@media (prefers-color-scheme: dark) :root) is missing");
        Assert.Equal(Array.Empty<string>(), ColourTokens.Where(token => !Declares(dark.Groups["body"].Value, token)));
    }

    [Fact]
    public void The_dark_theme_redefines_colours_only()
    {
        var dark = DarkBlock.Match(Stylesheet).Groups["body"].Value;

        Assert.Equal(Array.Empty<string>(), LayoutTokens.Where(token => Declares(dark, token)));
    }

    [Fact]
    public void Every_shared_class_of_the_contract_exists_in_the_stylesheet()
    {
        var css = Comments.Replace(Stylesheet, string.Empty);

        var missing = SharedClasses.Where(name => !Regex.IsMatch(css, $@"\.{Regex.Escape(name)}(?![\w-])")).ToArray();

        Assert.Equal(Array.Empty<string>(), missing);
    }

    [Fact]
    public void No_colour_appears_outside_the_token_blocks_in_any_stylesheet()
    {
        var stylesheets = new[] { Stylesheet }
            .Concat(Directory.EnumerateFiles(WebRoot, "*.razor.css", SearchOption.AllDirectories).Select(File.ReadAllText));

        var strays = stylesheets
            .Select(css => TokenBlocks.Replace(Comments.Replace(css, string.Empty), string.Empty))
            .SelectMany(css => new[] { Hex, ColourFunction, NamedColour }.SelectMany(rule => rule.Matches(css).Select(match => match.Value)))
            .ToArray();

        Assert.Equal(Array.Empty<string>(), strays);
    }

    [Fact]
    public void Focus_is_visible_and_motion_is_reduced_on_request()
    {
        Assert.Matches(@":focus-visible\s*\{[^}]*outline:", Stylesheet);
        Assert.Matches(@"@media \(prefers-reduced-motion: reduce\)", Stylesheet);
    }

    [Fact]
    public void Wide_tables_scroll_inside_their_own_box_not_the_page()
    {
        Assert.Matches(@"\.table-wrap\s*\{[^}]*overflow-x:\s*auto", Stylesheet);
    }

    [Fact]
    public void The_stylesheet_uses_system_font_stacks_only()
    {
        Assert.DoesNotMatch(@"@font-face|fonts\.googleapis|fonts\.gstatic|@import", Stylesheet);
    }

    private static bool Declares(string block, string token) =>
        Regex.IsMatch(block, $@"--{Regex.Escape(token)}\s*:");
}
