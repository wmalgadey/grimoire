namespace Grimoire.Domain.Guardrails;

/// <summary>
/// One write-scope rule (data-model.md "Write Rule", ADR-015): a canonical path prefix
/// plus whether it is <c>create-only</c> (denied by the harness when the canonical target
/// already exists on disk) or the default <c>read-write</c>.
/// </summary>
public readonly record struct WriteRule(string Prefix, bool CreateOnly);

/// <summary>
/// Deny-by-default safety policy evaluated for every guarded tool call.
/// All paths supplied to <see cref="Evaluate"/> MUST be pre-canonicalized
/// absolute paths with lexical normalization applied (<c>..</c> segments removed).
/// This type is dependency-free and pure — no I/O, no logging.
/// </summary>
public sealed class SafetyPolicy
{
    private readonly IReadOnlyList<string> _readPrefixes;
    private readonly IReadOnlyList<WriteRule> _writeRules;
    private readonly string _repositoryRoot;

    /// <summary>
    /// Initializes a policy with absolute-path canonical prefixes already resolved
    /// against the repository root. Every write prefix is plain <c>read-write</c> — use
    /// the <see cref="SafetyPolicy(string, IReadOnlyList{string}, IReadOnlyList{WriteRule})"/>
    /// overload to supply <c>create-only</c> rules (ADR-015).
    /// </summary>
    /// <param name="repositoryRoot">
    /// Canonical absolute path to the repository root, used for traversal detection.
    /// </param>
    /// <param name="readPrefixes">
    /// Canonical absolute path prefixes that allow read-scope tool calls.
    /// </param>
    /// <param name="writePrefixes">
    /// Canonical absolute path prefixes that allow write-scope tool calls.
    /// </param>
    public SafetyPolicy(
        string repositoryRoot,
        IReadOnlyList<string> readPrefixes,
        IReadOnlyList<string> writePrefixes)
        : this(repositoryRoot, readPrefixes, writePrefixes.Select(p => new WriteRule(p, CreateOnly: false)).ToList())
    {
    }

    /// <summary>
    /// Initializes a policy with absolute-path canonical prefixes already resolved
    /// against the repository root, and per-write-rule <c>create-only</c> mode (ADR-015).
    /// </summary>
    /// <param name="repositoryRoot">
    /// Canonical absolute path to the repository root, used for traversal detection.
    /// </param>
    /// <param name="readPrefixes">
    /// Canonical absolute path prefixes that allow read-scope tool calls.
    /// </param>
    /// <param name="writeRules">
    /// Canonical absolute path prefixes (each with its <c>create-only</c>/<c>read-write</c>
    /// mode) that allow write-scope tool calls.
    /// </param>
    public SafetyPolicy(
        string repositoryRoot,
        IReadOnlyList<string> readPrefixes,
        IReadOnlyList<WriteRule> writeRules)
    {
        _repositoryRoot = repositoryRoot;
        _readPrefixes = readPrefixes;
        _writeRules = writeRules;
    }

    /// <summary>
    /// Evaluates whether a canonicalized target path is permitted for the given tool scope.
    /// </summary>
    /// <param name="canonicalTarget">
    /// The absolute, lexically-normalized target path (no <c>..</c> segments).
    /// </param>
    /// <param name="isWrite">
    /// <c>true</c> to check write-scope rules; <c>false</c> for read-scope rules.
    /// </param>
    public PolicyDecision Evaluate(string canonicalTarget, bool isWrite)
    {
        // Traversal check: if the canonical target escapes the repository root,
        // deny regardless of any allow rules.
        if (!IsWithinRepositoryRoot(canonicalTarget))
        {
            return PolicyDecision.Deny("traversal");
        }

        if (!isWrite)
        {
            foreach (var prefix in _readPrefixes)
            {
                if (PrefixMatches(prefix, canonicalTarget))
                {
                    return PolicyDecision.Allow();
                }
            }

            return PolicyDecision.Deny("no_rule");
        }

        foreach (var rule in _writeRules)
        {
            if (PrefixMatches(rule.Prefix, canonicalTarget))
            {
                return PolicyDecision.Allow(isCreateOnly: rule.CreateOnly);
            }
        }

        return PolicyDecision.Deny("out_of_scope");
    }

    private bool IsWithinRepositoryRoot(string canonicalTarget)
    {
        var relative = Path.GetRelativePath(_repositoryRoot, canonicalTarget);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PrefixMatches(string prefix, string canonicalTarget)
    {
        if (prefix.EndsWith(Path.DirectorySeparatorChar))
        {
            // A directory rule also allows the directory itself as a target (e.g. a
            // list_files call on "pages"), whose canonicalized path never carries the
            // trailing separator the prefix does.
            return canonicalTarget.StartsWith(prefix, StringComparison.Ordinal)
                || canonicalTarget.Equals(prefix[..^1], StringComparison.Ordinal);
        }

        return canonicalTarget.Equals(prefix, StringComparison.Ordinal);
    }
}
