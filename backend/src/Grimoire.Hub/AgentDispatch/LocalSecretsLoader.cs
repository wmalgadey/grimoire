namespace Grimoire.Hub.AgentDispatch;

public sealed class LocalSecretsLoader
{
    private readonly string _envFilePath;

    public LocalSecretsLoader(string envFilePath)
    {
        _envFilePath = envFilePath;
    }

    public string? GetAnthropicAuthToken()
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
            if (parts.Length == 2 && parts[0] == "ANTHROPIC_AUTH_TOKEN")
            {
                return parts[1].Trim('"');
            }
        }

        return null;
    }
}
