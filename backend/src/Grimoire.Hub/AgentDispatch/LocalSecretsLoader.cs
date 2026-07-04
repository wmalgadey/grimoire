namespace Grimoire.Hub.AgentDispatch;

public sealed class LocalSecretsLoader
{
    private readonly string _envFilePath;

    public LocalSecretsLoader(string envFilePath)
    {
        _envFilePath = envFilePath;
    }

    public string? GetAnthropicAuthToken() => ReadEnvVar("ANTHROPIC_AUTH_TOKEN");

    /// <summary>
    /// Returns the effective ingest model override if set in the .env file, or null
    /// if the default should be used (ADR-004, R3).
    /// </summary>
    public string? GetIngestModel() => ReadEnvVar("GRIMOIRE_INGEST_MODEL");

    private string? ReadEnvVar(string varName)
    {
        if (!File.Exists(_envFilePath))
        {
            return null;
        }

        foreach (var rawLine in File.ReadLines(_envFilePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0] == varName)
            {
                return parts[1].Trim('"');
            }
        }

        return null;
    }
}
