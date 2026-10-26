using System.Diagnostics;
using PostmanClone.TestKit;
using Xunit;

namespace PostmanClone.ServiceDefaults.Tests.Kernel;

/// <summary>scripts/check-kernel-size.sh is the mechanical ceiling on the kernel (P2). These tests prove it can fail.</summary>
public sealed class KernelSizeTests : IDisposable
{
    private readonly string _fixtures = Path.Combine(Path.GetTempPath(), $"pc-kernel-size-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_fixtures))
        {
            Directory.Delete(_fixtures, recursive: true);
        }
    }

    [Fact]
    public void The_real_kernel_is_within_the_limit()
    {
        var (exitCode, output) = RunScript(RepoRoot.Find(), Path.Combine("src", "PostmanClone.ServiceDefaults"));

        Assert.Equal(0, exitCode);
        Assert.Contains("limit 800", output);
    }

    [Fact]
    public void A_fixture_of_801_code_lines_fails()
    {
        var directory = WriteFixture(codeLines: 801);

        var (exitCode, output) = RunScript(RepoRoot.Find(), directory);

        Assert.Equal(1, exitCode);
        Assert.Contains("801 non-blank, non-comment lines", output);
    }

    [Fact]
    public void A_fixture_of_exactly_800_code_lines_passes()
    {
        var directory = WriteFixture(codeLines: 800);

        var (exitCode, _) = RunScript(RepoRoot.Find(), directory);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Blank_and_comment_lines_do_not_count()
    {
        var directory = WriteFixture(codeLines: 800, commentLines: 300, blankLines: 100, blockCommentLines: 50);

        var (exitCode, output) = RunScript(RepoRoot.Find(), directory);

        Assert.Equal(0, exitCode);
        Assert.Contains("800 non-blank, non-comment lines", output);
    }

    private string WriteFixture(int codeLines, int commentLines = 0, int blankLines = 0, int blockCommentLines = 0)
    {
        var directory = Path.Combine(_fixtures, "kernel");
        Directory.CreateDirectory(directory);

        var lines = new List<string>();
        lines.AddRange(Enumerable.Range(0, codeLines).Select(i => $"var value{i} = {i};"));
        lines.AddRange(Enumerable.Repeat("// a comment line", commentLines));
        lines.AddRange(Enumerable.Repeat(string.Empty, blankLines));
        lines.Add("/*");
        lines.AddRange(Enumerable.Repeat("  block comment text", blockCommentLines));
        lines.Add("*/");

        File.WriteAllLines(Path.Combine(directory, "Fixture.cs"), lines);
        return directory;
    }

    private static (int ExitCode, string Output) RunScript(string repoRoot, string directory)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(repoRoot, "scripts", "check-kernel-size.sh"));
        startInfo.ArgumentList.Add(directory);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
