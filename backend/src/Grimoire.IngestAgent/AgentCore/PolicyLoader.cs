using Grimoire.Domain.Guardrails;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire.IngestAgent.AgentCore;

/// <summary>Identity record for a loaded safety policy file (FR-012).</summary>
public sealed record PolicyIdentity(string Path, int Version, string Sha256);

/// <summary>Result of a successful policy load.</summary>
public sealed record LoadedPolicy(SafetyPolicy Policy, PolicyIdentity Identity);

/// <summary>Failure record returned instead of throwing (fail-closed loading).</summary>
public sealed record PolicyLoadFailure(string Reason);

/// <summary>
/// Loads and validates the deny-by-default safety policy from a JSON file.
/// Fail-closed: any parse error, schema violation, or missing file returns a failure
/// result — never a default/permissive policy.
/// </summary>
public sealed class PolicyLoader
{
    private readonly string _wikiRoot;

    /// <param name="wikiRoot">
    /// The Hub-resolved wiki content root (contracts/agent-launch.md <c>--wiki-root</c>).
    /// Policy path prefixes (<c>pages/</c>, <c>tasks/</c>, <c>index.md</c>, <c>log.md</c>)
    /// are anchored here — never against a discovered repository root (ADR-009).
    /// </param>
    public PolicyLoader(string wikiRoot)
    {
        _wikiRoot = wikiRoot;
    }

    /// <summary>
    /// Loads the policy at <paramref name="policyPath"/>, resolves prefixes against the
    /// wiki root, and returns either a loaded policy or a failure.
    /// </summary>
    public async Task<OneOf<LoadedPolicy, PolicyLoadFailure>> LoadAsync(
        string policyPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(policyPath))
        {
            return new PolicyLoadFailure($"Policy file not found: {policyPath}");
        }

        byte[] fileBytes;
        try
        {
            fileBytes = await File.ReadAllBytesAsync(policyPath, cancellationToken);
        }
        catch (Exception ex)
        {
            return new PolicyLoadFailure($"Cannot read policy file '{policyPath}': {ex.Message}");
        }

        PolicyFileSchema? schema;
        try
        {
            schema = JsonSerializer.Deserialize<PolicyFileSchema>(fileBytes, _jsonOptions);
        }
        catch (JsonException ex)
        {
            return new PolicyLoadFailure($"Policy file '{policyPath}' is not valid JSON: {ex.Message}");
        }

        if (schema is null)
        {
            return new PolicyLoadFailure($"Policy file '{policyPath}' deserialised to null.");
        }

        if (schema.DefaultDecision is null ||
            !string.Equals(schema.DefaultDecision, "deny", StringComparison.Ordinal))
        {
            return new PolicyLoadFailure(
                $"Policy file '{policyPath}': defaultDecision must be \"deny\" (got \"{schema.DefaultDecision}\").");
        }

        if (schema.Version < 1)
        {
            return new PolicyLoadFailure(
                $"Policy file '{policyPath}': version must be >= 1 (got {schema.Version}).");
        }

        var readPrefixes = ResolveAndNormalize(schema.Read ?? [], isWrite: false);
        var writePrefixes = ResolveAndNormalize(schema.Write ?? [], isWrite: true);
        var policy = new SafetyPolicy(_wikiRoot, readPrefixes, writePrefixes);

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
        var identity = new PolicyIdentity(policyPath, schema.Version, sha256);

        return new LoadedPolicy(policy, identity);
    }

    private IReadOnlyList<string> ResolveAndNormalize(
        IReadOnlyList<PolicyRuleSchema> rules,
        bool isWrite)
    {
        var prefixes = new List<string>(rules.Count);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.PathPrefix))
                continue;

            var platformPathPrefix = rule.PathPrefix
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            // Resolve relative prefix against the wiki root (ADR-009).
            var absolute = Path.IsPathRooted(platformPathPrefix)
                ? platformPathPrefix
                : Path.Combine(_wikiRoot, platformPathPrefix);
            var canonical = Path.GetFullPath(absolute);

            // Ensure directory prefixes end with the directory separator so prefix
            // matching does not accidentally permit sibling paths.
            var normalized = canonical;
            if (platformPathPrefix.EndsWith(Path.DirectorySeparatorChar))
            {
                normalized = normalized.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            }

            prefixes.Add(normalized);
        }
        return prefixes;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
    };

    // ── Private schema types (not part of domain model) ──────────────────────────

    private sealed class PolicyFileSchema
    {
        public int Version { get; set; }
        public string? DefaultDecision { get; set; }
        public IReadOnlyList<PolicyRuleSchema>? Read { get; set; }
        public IReadOnlyList<PolicyRuleSchema>? Write { get; set; }
    }

    private sealed class PolicyRuleSchema
    {
        public string PathPrefix { get; set; } = string.Empty;
    }
}

/// <summary>
/// Minimal discriminated-union helper for load results (avoids exceptions for
/// control flow in the fail-closed loader).
/// </summary>
public readonly struct OneOf<T1, T2>
{
    private readonly T1? _v1;
    private readonly T2? _v2;
    private readonly bool _isFirst;

    private OneOf(T1 v1) { _v1 = v1; _isFirst = true; _v2 = default; }
    private OneOf(T2 v2) { _v2 = v2; _isFirst = false; _v1 = default; }

    public static implicit operator OneOf<T1, T2>(T1 v) => new(v);
    public static implicit operator OneOf<T1, T2>(T2 v) => new(v);

    public bool IsFirst(out T1 value)
    {
        value = _v1!;
        return _isFirst;
    }

    public bool IsSecond(out T2 value)
    {
        value = _v2!;
        return !_isFirst;
    }
}
