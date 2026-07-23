namespace Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;

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

    /// <summary>
    /// Returns the ingest loop token cap override if set in the .env file, or null
    /// if the agent default should be used.
    /// </summary>
    public string? GetIngestTokenCap() => ReadEnvVar("GRIMOIRE_INGEST_TOKEN_CAP");

    internal string? GetIngestBase() => ReadEnvVar("GRIMOIRE_INGEST_BASE_URL");

    /// <summary>Query's own model override (ADR-004 applied to 008-query-agent), independent of Ingest's.</summary>
    public string? GetQueryModel() => ReadEnvVar("GRIMOIRE_QUERY_MODEL");

    internal string? GetQueryBase() => ReadEnvVar("GRIMOIRE_QUERY_BASE_URL");

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
