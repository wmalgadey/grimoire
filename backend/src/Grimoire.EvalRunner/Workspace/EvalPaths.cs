namespace Grimoire.EvalRunner.Workspace;

/// <summary>
/// Repository-anchored input locations for eval runs (ADR-022: instructions resolve from
/// the agent project SOURCES — never the runtime agent directory and never build output —
/// so an eval run needs neither a prior agent build nor any hub configuration, FR-017/
/// FR-018/SC-010; recordings resolve from a fixture folder inside the test project,
/// FR-016/SC-009; the runner resolves everything once from the repo root and passes
/// copies into each isolated workspace).
/// </summary>
public sealed record EvalPaths(string RepoRoot)
{
    public string AgentInstructionsDir => Path.Combine(RepoRoot, "backend", "src", "Grimoire.IngestAgent", "Instructions");

    public string SystemPromptPath => Path.Combine(AgentInstructionsDir, "system-prompt.md");

    public string DefaultUserPromptPath => Path.Combine(AgentInstructionsDir, "default-user-prompt.md");

    public string PolicyPath => Path.Combine(AgentInstructionsDir, "policy.json");

    // Query's own instruction surface (ADR-007 pattern, ADR-011) — a sibling of Ingest's,
    // with no default-user-prompt document (the user's Query Prompt is always supplied
    // per turn, research.md R1 of 008-query-agent).
    public string QueryInstructionsDir => Path.Combine(RepoRoot, "backend", "src", "Grimoire.QueryAgent", "Instructions");

    public string QuerySystemPromptPath => Path.Combine(QueryInstructionsDir, "system-prompt.md");

    public string QueryPolicyPath => Path.Combine(QueryInstructionsDir, "policy.json");

    // Lint's own instruction surface (013-lint-agent, ADR-013 pattern) — a sibling of
    // Query's, also with no default-user-prompt document (Lint takes no per-run input
    // at all; the whole wiki is its input, per LintCliOptions).
    public string LintInstructionsDir => Path.Combine(RepoRoot, "backend", "src", "Grimoire.LintAgent", "Instructions");

    public string LintSystemPromptPath => Path.Combine(LintInstructionsDir, "system-prompt.md");

    public string LintPolicyPath => Path.Combine(LintInstructionsDir, "policy.json");

    public string FixturesRoot => Path.Combine(RepoRoot, "backend", "tests", "Grimoire.AgentEvals", "Fixtures");

    public string FixtureWikiRoot(string fixtureName) => Path.Combine(FixturesRoot, fixtureName, "wiki");

    public string RecordingsRoot => Path.Combine(FixturesRoot, "recordings");

    public string LocalEnvPath => Path.Combine(RepoRoot, ".env");

    public static EvalPaths Discover(string? start = null)
    {
        var current = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))
                || Directory.Exists(Path.Combine(current.FullName, ".specify")))
            {
                return new EvalPaths(current.FullName);
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate the repository root for eval runs.");
    }
}
