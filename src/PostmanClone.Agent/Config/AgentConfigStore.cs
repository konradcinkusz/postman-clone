using System.Text.Json;
using System.Text.Json.Serialization;

namespace PostmanClone.Agent.Config;

/// <summary>
/// Reads and writes <see cref="AgentConfig"/> as one JSON file in the user's own configuration folder (ADR-0026 Decision 11).
/// The file is written atomically and, on Unix, given mode 0600 and its folder 0700. On Windows the file takes the user's profile
/// ACL; a stricter ACL is an open item (OPEN-ITEMS, agent token storage).
/// </summary>
public sealed class AgentConfigStore
{
    public const string HomeVariable = "POSTMANCLONE_AGENT_HOME";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _directory;

    public AgentConfigStore(string? directory = null)
    {
        _directory = directory
            ?? Environment.GetEnvironmentVariable(HomeVariable)
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "postmanclone-agent");
    }

    public string FilePath => Path.Combine(_directory, "agent.json");

    public AgentConfig Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AgentConfig();
        }

        return JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(FilePath), Json) ?? new AgentConfig();
    }

    public void Save(AgentConfig config)
    {
        Directory.CreateDirectory(_directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, Json));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporary, FilePath, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
