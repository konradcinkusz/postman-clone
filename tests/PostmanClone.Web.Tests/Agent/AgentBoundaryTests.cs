using PostmanClone.TestKit;

namespace PostmanClone.Web.Tests.Agent;

/// <summary>
/// What the agent work may add to the Web, read from disk (WP31, ADR-0009, ADR-0028): one hand-written script and no other; no
/// package; the page's script tags are Blazor's and the agent's; and the script never writes the page or runs text as code.
/// </summary>
public sealed class AgentBoundaryTests
{
    private static readonly string WebRoot = Path.Combine(RepoRoot.Find(), "src", "PostmanClone.Web");

    [Fact]
    public void The_Web_serves_exactly_one_script_of_its_own_and_it_is_the_agent_client()
    {
        var scripts = Directory.EnumerateFiles(Path.Combine(WebRoot, "wwwroot"), "*.js", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(Path.Combine(WebRoot, "wwwroot"), file).Replace('\\', '/'))
            .Order()
            .ToArray();

        Assert.Equal(["agent-client.js"], scripts);
    }

    [Fact]
    public void The_page_loads_the_agent_client_and_Blazors_script_and_nothing_else()
    {
        var app = File.ReadAllText(Path.Combine(WebRoot, "Components", "App.razor"));

        var sources = app.Split('\n')
            .Where(line => line.Contains("<script", StringComparison.Ordinal))
            .Select(line => System.Text.RegularExpressions.Regex.Match(line, @"Assets\[""([^""]+)""\]").Groups[1].Value)
            .ToArray();

        Assert.Equal(["agent-client.js", "_framework/blazor.web.js"], sources);
    }

    [Fact]
    public void The_Web_adds_no_package_reference()
    {
        var project = File.ReadAllText(Path.Combine(WebRoot, "PostmanClone.Web.csproj"));

        Assert.DoesNotContain("PackageReference", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_writes_no_page_content_and_runs_no_text_as_code()
    {
        var script = File.ReadAllText(Path.Combine(WebRoot, "wwwroot", "agent-client.js"));

        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", script, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("new Function", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_never_reads_the_session_cookie_or_the_session_token()
    {
        var script = File.ReadAllText(Path.Combine(WebRoot, "wwwroot", "agent-client.js"));

        Assert.DoesNotContain("document.cookie", script, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", script, StringComparison.Ordinal);
    }
}
