namespace PostmanClone.Web.E2E.Support;

/// <summary>
/// The stack's folder and its logs (.local-stack/logs, written by scripts/run-local-stack.sh). A journey reads a log only to check
/// something the page does not show: the agent's output never holds a secret, and an invitation token is in authservice's log when
/// the stack runs in Development (docs/AUTH.md).
/// </summary>
public static class StackLogs
{
    /// <summary>The .local-stack folder. It is found from the test binaries, which sit several folders below the repository root.</summary>
    public static string Directory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !System.IO.Directory.Exists(Path.Combine(directory.FullName, ".local-stack")))
            {
                directory = directory.Parent;
            }

            return directory is null
                ? throw new InvalidOperationException("The .local-stack folder was not found above the test binaries. Run scripts/run-local-stack.sh up first.")
                : Path.Combine(directory.FullName, ".local-stack");
        }
    }

    /// <summary>The whole text of a log written by the stack. Empty when it does not exist yet.</summary>
    public static string ReadText(string name)
    {
        var path = Path.Combine(Directory, "logs", name);
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        // The services keep writing while the journey reads, so the file is opened shared.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
