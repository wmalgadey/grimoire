namespace Grimoire.AgentRuntime.Composition;

/// <summary>
/// The shared `--key value` CLI parsing scaffold (ADR-002 spawn contract; ADR-013 —
/// consolidates the formerly duplicated ParseArgs loop/helpers in both hosts'
/// Program.cs). Each host keeps only its own option record (IngestCliOptions /
/// QueryCliOptions) and maps required/optional arguments through this reader.
/// Unknown-argument and missing-argument behavior is unchanged: pairs not starting
/// with `--` are ignored, missing required arguments throw the exact pre-consolidation
/// error text.
/// </summary>
public sealed class AgentArgumentReader
{
    private readonly Dictionary<string, string> _options;

    public AgentArgumentReader(string[] args)
    {
        _options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length - 1; i += 2)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                _options[args[i]] = args[i + 1];
        }
    }

    public string GetRequired(string name)
        => _options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument {name}");

    public string? GetOptional(string name)
        => _options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>The shared `--heartbeat-seconds` option with its frozen default of 10.</summary>
    public int GetHeartbeatSeconds()
        => int.TryParse(GetOptional("--heartbeat-seconds"), out var parsedHeartbeat) && parsedHeartbeat > 0
            ? parsedHeartbeat
            : 10;
}
