namespace Grimoire.Domain.Guardrails;

/// <summary>
/// Write-scope mode for one <see cref="WriteRule"/> (ADR-015, extended by ADR-016):
/// <list type="bullet">
/// <item><see cref="ReadWrite"/> (default): any content change to the target is
/// permitted, subject only to <c>SharedFileWriteGuard</c>'s compare-and-swap check.</item>
/// <item><see cref="CreateOnly"/>: the harness denies the write
/// (<c>create_only_target_exists</c>) if the canonical target already exists on disk.</item>
/// <item><see cref="FrontmatterOnly"/> (ADR-016, 013-lint-agent): the harness denies the
/// write (<c>frontmatter_only_target_missing</c>) if the canonical target does NOT
/// already exist, then — after the same compare-and-swap check <see cref="ReadWrite"/>
/// uses — denies (<c>frontmatter_only_malformed_document</c> /
/// <c>frontmatter_only_body_changed</c>) unless the proposed content's body (everything
/// after the closing <c>---</c> frontmatter delimiter) is byte-identical to the current
/// on-disk body. Only the frontmatter block may change.</item>
/// </list>
/// </summary>
public enum WriteMode
{
    ReadWrite,
    CreateOnly,
    FrontmatterOnly,
}

/// <summary>
/// One write-scope rule (data-model.md "Write Rule", ADR-015, extended by ADR-016): a
/// canonical path prefix plus its <see cref="Mode"/>.
/// </summary>
public readonly record struct WriteRule
{
    public string Prefix { get; }

    public WriteMode Mode { get; }

    /// <summary>
    /// Exact-match canonical paths this rule never matches, even though
    /// <see cref="Prefix"/> otherwise would (014-wiki-storage-restructure, R3 correction).
    /// A directory-style root prefix (e.g. <c>"."</c>, the whole wiki content root) has no
    /// way to express "everything except these two files" other than this: the policy
    /// schema is allow-list-only with first-match-wins and no deny-rule concept, so an
    /// agent whose write rules contain no separate, earlier entry for a reserved file
    /// (e.g. Lint, which should never write <c>index.md</c>/<c>log.md</c> at all) would
    /// otherwise have that file incorrectly fall inside a broad catch-all's mode instead
    /// of correctly falling through to <c>defaultDecision: deny</c> ("out_of_scope").
    /// Always resolved as exact-match, independent of the rule's own prefix shape.
    /// </summary>
    public IReadOnlyList<string> ExcludePrefixes { get; }

    /// <summary>
    /// Pre-ADR-016 computed convenience: <c>true</c> iff <see cref="Mode"/> is
    /// <see cref="WriteMode.CreateOnly"/>. Retained so every call site and test written
    /// against the boolean shape (before ADR-016 introduced the three-way <see cref="Mode"/>)
    /// keeps compiling and passing unchanged.
    /// </summary>
    public bool CreateOnly => Mode == WriteMode.CreateOnly;

    public WriteRule(string Prefix, WriteMode Mode = WriteMode.ReadWrite, IReadOnlyList<string>? ExcludePrefixes = null)
    {
        this.Prefix = Prefix;
        this.Mode = Mode;
        this.ExcludePrefixes = ExcludePrefixes ?? Array.Empty<string>();
    }

    /// <summary>
    /// Pre-ADR-016 boolean constructor, retained for source compatibility with every
    /// existing call site (e.g. <c>new WriteRule(prefix, CreateOnly: true)</c>).
    /// </summary>
    public WriteRule(string Prefix, bool CreateOnly)
        : this(Prefix, CreateOnly ? WriteMode.CreateOnly : WriteMode.ReadWrite)
    {
    }
}

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
    /// overload to supply <c>create-only</c>/<c>frontmatter-only</c> rules (ADR-015/ADR-016).
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
    /// against the repository root, and per-write-rule mode (ADR-015/ADR-016).
    /// </summary>
    /// <param name="repositoryRoot">
    /// Canonical absolute path to the repository root, used for traversal detection.
    /// </param>
    /// <param name="readPrefixes">
    /// Canonical absolute path prefixes that allow read-scope tool calls.
    /// </param>
    /// <param name="writeRules">
    /// Canonical absolute path prefixes (each with its <see cref="WriteMode"/>) that allow
    /// write-scope tool calls.
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
            if (PrefixMatches(rule.Prefix, canonicalTarget) && !IsExcluded(rule.ExcludePrefixes, canonicalTarget))
            {
                return PolicyDecision.Allow(rule.Mode);
            }
        }

        return PolicyDecision.Deny("out_of_scope");
    }

    private static bool IsExcluded(IReadOnlyList<string> excludePrefixes, string canonicalTarget)
    {
        foreach (var excluded in excludePrefixes)
        {
            if (canonicalTarget.Equals(excluded, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private bool IsWithinRepositoryRoot(string canonicalTarget)
    {
        var relative = Path.GetRelativePath(_repositoryRoot, canonicalTarget);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    /// <summary>
    /// 015-lint-board-parity T042 (ADR-011 Query-turn shape, ADR-018 message-turn mode):
    /// a genuine deny-by-default clone of this policy with every write rule stripped —
    /// <see cref="Evaluate"/> then denies any write-scope call with <c>out_of_scope</c>,
    /// regardless of target. Read rules (and the repository-root traversal check) are
    /// preserved unchanged, so the agent can still browse the wiki to answer questions
    /// about a proposal. Used to make a message turn structurally read-only (Constitution
    /// V: guardrails enforced at the tool boundary, not left to instruction-following
    /// alone) without introducing a second on-disk policy file or CLI path — the same
    /// loaded policy identity (version/sha256) still describes what was read from disk;
    /// this method only changes what the in-memory <see cref="SafetyPolicy"/> instance
    /// enforces for that one run.
    /// </summary>
    public SafetyPolicy WithNoWriteAccess() => new(_repositoryRoot, _readPrefixes, Array.Empty<WriteRule>());

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
