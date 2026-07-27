namespace Grimoire.EvalRunner.Workspace;

/// <summary>
/// Repository-anchored input locations for eval runs (ADR-009: paths are explicit; the
/// runner resolves them once from the repo root and passes copies into each isolated
/// workspace).
/// </summary>
public sealed record EvalPaths(string RepoRoot)
{
    public string AgentInstructionsDir => Path.Combine(RepoRoot, "data", "agents", "ingest");

    public string SystemPromptPath => Path.Combine(AgentInstructionsDir, "system-prompt.md");

    public string DefaultUserPromptPath => Path.Combine(AgentInstructionsDir, "default-user-prompt.md");

    public string PolicyPath => Path.Combine(AgentInstructionsDir, "policy.json");

    // Query's own instruction surface (ADR-007 pattern, ADR-011) — a sibling of Ingest's,
    // with no default-user-prompt document (the user's Query Prompt is always supplied
    // per turn, research.md R1 of 008-query-agent).
    public string QueryInstructionsDir => Path.Combine(RepoRoot, "data", "agents", "query");

    public string QuerySystemPromptPath => Path.Combine(QueryInstructionsDir, "system-prompt.md");

    public string QueryPolicyPath => Path.Combine(QueryInstructionsDir, "policy.json");

    public string FixturesRoot => Path.Combine(RepoRoot, "backend", "tests", "Grimoire.AgentEvals", "Fixtures");

    public string FixtureWikiRoot(string fixtureName) => Path.Combine(FixturesRoot, fixtureName, "wiki");

    public string DefaultRecordingsRoot => Path.Combine(RepoRoot, "data", "evals", "recordings");

    public string LocalEnvPath => Path.Combine(RepoRoot, "data", ".env");

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
