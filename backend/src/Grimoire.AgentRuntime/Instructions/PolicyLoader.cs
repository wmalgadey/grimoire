using Grimoire.Domain.Guardrails;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grimoire.AgentRuntime.Instructions;

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
    /// Policy path prefixes (<c>.</c>, <c>index.md</c>, <c>log.md</c>) are anchored
    /// here — never against a discovered repository root (ADR-009).
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

        var readPrefixes = ResolveAndNormalize(schema.Read ?? []);

        IReadOnlyList<WriteRule> writeRules;
        try
        {
            writeRules = ResolveAndNormalizeWriteRules(schema.Write ?? []);
        }
        catch (PolicyModeException ex)
        {
            return new PolicyLoadFailure($"Policy file '{policyPath}': {ex.Message}");
        }

        var deleteRules = ResolveAndNormalizeDeleteRules(schema.Delete ?? []);

        var policy = new SafetyPolicy(_wikiRoot, readPrefixes, writeRules, deleteRules);

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
        var identity = new PolicyIdentity(policyPath, schema.Version, sha256);

        return new LoadedPolicy(policy, identity);
    }

    private const string ReadWriteMode = "read-write";
    private const string CreateOnlyMode = "create-only";
    private const string FrontmatterOnlyMode = "frontmatter-only";

    /// <summary>Thrown internally to fail closed on an unrecognized <c>mode</c> value; never escapes <see cref="LoadAsync"/>.</summary>
    private sealed class PolicyModeException(string message) : Exception(message);

    private IReadOnlyList<string> ResolveAndNormalize(IReadOnlyList<PolicyRuleSchema> rules)
    {
        var prefixes = new List<string>(rules.Count);
        foreach (var rule in rules)
        {
            var normalized = NormalizeRulePrefix(rule);
            if (normalized is not null)
            {
                prefixes.Add(normalized);
            }
        }
        return prefixes;
    }

    /// <summary>
    /// Resolves write-scope rules, carrying each rule's <c>mode</c> (ADR-015, extended by
    /// ADR-016) into a <see cref="WriteRule"/>. <c>mode</c> is optional: absent or
    /// <c>"read-write"</c> means plain read-write (byte-for-byte the pre-ADR-015 behavior);
    /// <c>"create-only"</c> marks the rule create-only; <c>"frontmatter-only"</c> (ADR-016,
    /// 013-lint-agent) marks the rule frontmatter-only; any other value fails closed
    /// (<see cref="PolicyModeException"/>), matching the existing <c>defaultDecision</c>
    /// strictness — never silently defaulted.
    /// </summary>
    private IReadOnlyList<WriteRule> ResolveAndNormalizeWriteRules(IReadOnlyList<PolicyRuleSchema> rules)
    {
        var writeRules = new List<WriteRule>(rules.Count);
        foreach (var rule in rules)
        {
            var normalized = NormalizeRulePrefix(rule);
            if (normalized is null)
                continue;

            var mode = rule.Mode switch
            {
                null => WriteMode.ReadWrite,
                ReadWriteMode => WriteMode.ReadWrite,
                CreateOnlyMode => WriteMode.CreateOnly,
                FrontmatterOnlyMode => WriteMode.FrontmatterOnly,
                _ => throw new PolicyModeException(
                    $"write rule for \"{rule.PathPrefix}\" has unrecognized mode \"{rule.Mode}\" " +
                    $"(expected \"{ReadWriteMode}\", \"{CreateOnlyMode}\", or \"{FrontmatterOnlyMode}\")."),
            };

            var excludePrefixes = (rule.ExcludePrefixes ?? [])
                .Select(NormalizeExactPrefix)
                .ToList();

            writeRules.Add(new WriteRule(normalized, mode, excludePrefixes));
        }
        return writeRules;
    }

    /// <summary>
    /// Resolves delete-scope rules (ADR-031 R3, 026-guarded-tool-surface): deny-by-default,
    /// like read/write. Deliberately no <c>mode</c> handling — <see cref="DeleteRule"/> has
    /// no variants, so <see cref="PolicyRuleSchema.Mode"/> is simply ignored for these rules
    /// (a policy author who mistakenly writes a <c>mode</c> on a delete rule gets silent
    /// disregard, matching how <c>mode</c> is already ignored on read rules).
    /// </summary>
    private IReadOnlyList<DeleteRule> ResolveAndNormalizeDeleteRules(IReadOnlyList<PolicyRuleSchema> rules)
    {
        var deleteRules = new List<DeleteRule>(rules.Count);
        foreach (var rule in rules)
        {
            var normalized = NormalizeRulePrefix(rule);
            if (normalized is null)
                continue;

            var excludePrefixes = (rule.ExcludePrefixes ?? [])
                .Select(NormalizeExactPrefix)
                .ToList();

            deleteRules.Add(new DeleteRule(normalized, excludePrefixes));
        }
        return deleteRules;
    }

    /// <summary>
    /// Resolves an <c>excludePrefixes</c> entry (014-wiki-storage-restructure, R3
    /// correction) — always exact-match, anchored at <see cref="_wikiRoot"/> the same way
    /// <see cref="NormalizeRulePrefix"/> resolves a plain (non-directory-style) prefix,
    /// but without the <c>"."</c> root-prefix special case, which is meaningless here.
    /// </summary>
    private string NormalizeExactPrefix(string rawPrefix)
    {
        var platformPrefix = rawPrefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var absolute = Path.IsPathRooted(platformPrefix)
            ? platformPrefix
            : Path.Combine(_wikiRoot, platformPrefix);
        return Path.GetFullPath(absolute);
    }

    private string? NormalizeRulePrefix(PolicyRuleSchema rule)
    {
        if (string.IsNullOrWhiteSpace(rule.PathPrefix))
            return null;

        var platformPathPrefix = rule.PathPrefix
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        // "." denotes the wiki root itself (014-wiki-storage-restructure, R3), now that
        // articles live directly under it with no "pages/" wrapper. Treated directory-style
        // — matching the wiki root and everything beneath it — the same way a trailing-slash
        // prefix is treated below, by ensuring the normalized value ends with the directory
        // separator so SafetyPolicy.PrefixMatches takes its StartsWith (directory) branch.
        if (platformPathPrefix == ".")
        {
            return Path.GetFullPath(_wikiRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

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

        return normalized;
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

        /// <summary>ADR-031 R3 (026-guarded-tool-surface): deny-by-default, absent by
        /// default. Only Lint's policy declares this scope.</summary>
        public IReadOnlyList<PolicyRuleSchema>? Delete { get; set; }
    }

    private sealed class PolicyRuleSchema
    {
        public string PathPrefix { get; set; } = string.Empty;

        /// <summary>
        /// ADR-015 (extended by ADR-016): optional write-rule mode. Recognized:
        /// <c>"read-write"</c> (or absent, the pre-ADR-015 default), <c>"create-only"</c>,
        /// and <c>"frontmatter-only"</c> (ADR-016, 013-lint-agent). Any other value is a
        /// fail-closed load error — never silently defaulted. Ignored for read rules.
        /// </summary>
        public string? Mode { get; set; }

        /// <summary>
        /// Optional exact-match exclusions from this rule's prefix match
        /// (014-wiki-storage-restructure, R3 correction) — e.g. a directory-style
        /// <c>"."</c> write rule excluding <c>"index.md"</c>/<c>"log.md"</c> so an agent
        /// whose policy grants no separate rule for those two files never matches them via
        /// the catch-all, preserving <c>"out_of_scope"</c> as their denial reason. Ignored
        /// for read rules (matching them via a catch-all read rule is harmless).
        /// </summary>
        public IReadOnlyList<string>? ExcludePrefixes { get; set; }
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
