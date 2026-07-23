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

    public string FixturesRoot => Path.Combine(RepoRoot, "backend", "tests", "Grimoire.AgentEvals", "Fixtures");

    public string FixtureWikiRoot(string fixtureName) => Path.Combine(FixturesRoot, fixtureName, "wiki");

    public string DefaultRecordingsRoot => Path.Combine(RepoRoot, "data", "evals", "recordings");

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
