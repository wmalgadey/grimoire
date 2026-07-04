using Grimoire.IngestAgent;
using Grimoire.IngestAgent.Instructions;

namespace Grimoire.IntegrationTests;

public class InstructionContextTests
{
    [Fact]
    public async Task InstructionContextLoader_Loads_IngestAgentInstructionBundle()
    {
        var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
        var options = new AgentCliOptions(
            TaskId: "test-task",
            SourceRef: "source.md",
            SourceKind: "file",
            PagesDir: Path.Combine(repoRoot, "wiki", "pages"),
            TasksDir: Path.Combine(repoRoot, "wiki", "tasks"),
            IndexPath: Path.Combine(repoRoot, "wiki", "index.md"),
            LogPath: Path.Combine(repoRoot, "wiki", "log.md"),
            GuardrailPolicyPath: Path.Combine(repoRoot, "wiki", "policy", "ingest-guardrails.yml"),
            InstructionsRoot: repoRoot,
            SkillPaths: [],
            SkillName: "ingest-wiki-structure",
            DryRun: true,
            PastedText: null);

        var snapshot = await new InstructionContextLoader().LoadAsync(options, CancellationToken.None);

        Assert.Equal("loaded", snapshot.Status);
        Assert.EndsWith("backend/src/Grimoire.IngestAgent/InstructionSet/CLAUDE.md", snapshot.ClaudePath.Replace('\\', '/'));
        Assert.NotEmpty(snapshot.SkillPaths);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ContentHash));
    }

    private static string FindRepoRoot(string start)
    {
        var current = Path.GetFullPath(start);
        while (true)
        {
            if (Directory.Exists(Path.Combine(current, ".specify")) && Directory.Exists(Path.Combine(current, "specs")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                throw new InvalidOperationException("Could not find repository root.");
            }

            current = parent.FullName;
        }
    }
}
