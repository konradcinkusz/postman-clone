namespace PostmanClone.TestKit;

/// <summary>Finds the repository root from the test binary's folder, so tests can run repository scripts.</summary>
public static class RepoRoot
{
    private const string SolutionFile = "PostmanClone.sln";

    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"{SolutionFile} was not found above {AppContext.BaseDirectory}.");
    }
}
