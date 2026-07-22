namespace Grimoire.Domain.Guardrails;

/// <summary>
/// Deny-by-default safety policy evaluated for every guarded tool call.
/// All paths supplied to <see cref="Evaluate"/> MUST be pre-canonicalized
/// absolute paths with lexical normalization applied (<c>..</c> segments removed).
/// This type is dependency-free and pure — no I/O, no logging.
/// </summary>
public sealed class SafetyPolicy
{
    private readonly IReadOnlyList<string> _readPrefixes;
    private readonly IReadOnlyList<string> _writePrefixes;
    private readonly string _repositoryRoot;

    /// <summary>
    /// Initializes a policy with absolute-path canonical prefixes already resolved
    /// against the repository root.
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
    {
        _repositoryRoot = repositoryRoot;
        _readPrefixes = readPrefixes;
        _writePrefixes = writePrefixes;
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

        var prefixes = isWrite ? _writePrefixes : _readPrefixes;

        foreach (var prefix in prefixes)
        {
            if (PrefixMatches(prefix, canonicalTarget))
            {
                return PolicyDecision.Allow();
            }
        }

        return PolicyDecision.Deny(isWrite ? "out_of_scope" : "no_rule");
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
