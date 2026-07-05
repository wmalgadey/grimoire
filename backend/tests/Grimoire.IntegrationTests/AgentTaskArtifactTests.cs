using Grimoire.IngestAgent.TaskArtifact;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T051 — task-artifact body contract (contracts/task-artifact-format.md):
/// completed artifacts that recorded denials carry a "## Denied actions" body
/// section mirroring the denied_actions frontmatter (US3/AC3).
/// </summary>
public class AgentTaskArtifactTests
{
    private static TaskArtifactDocument CompletedDocument(IReadOnlyList<DeniedActionEntry>? deniedActions) =>
        new(
            TaskId: "task-artifact-1",
            Type: "ingest",
            Status: "completed",
            Agent: "ingest",
            StartedAt: DateTimeOffset.Parse("2026-07-05T10:00:00Z"),
            CompletedAt: DateTimeOffset.Parse("2026-07-05T10:05:00Z"),
            SourceRef: "source.md",
            PagesTouched: ["wiki/pages/topic.md"],
            FailureReason: null,
            Narrative: "Updated the topic page because the source refined it.",
            PagesCreated: [],
            PagesUpdated: ["wiki/pages/topic.md"],
            PagesSuperseded: [],
            DeniedActions: deniedActions,
            InstructionFiles: [new InstructionFileRecord("agents/ingest/CLAUDE.md", "abc123")],
            Policy: new PolicyRecord("agents/ingest/policy.json", 1, "def456"),
            Model: "fake-model",
            Turns: 3,
            RolledBack: null);

    [Fact]
    public async Task CompletedArtifact_WithDenials_AppendsDeniedActionsBodySection()
    {
        var root = Path.Combine(Path.GetTempPath(), $"artifact-body-{Guid.NewGuid():N}");
        var artifactPath = Path.Combine(root, "tasks", "task-artifact-1.md");
        var denial = new DeniedActionEntry(
            "write_file", "../secrets.txt", "/etc/secrets.txt", "traversal", 2);

        var store = new TaskArtifactStore();
        await store.WriteAsync(artifactPath, CompletedDocument([denial]), CancellationToken.None);

        var content = await File.ReadAllTextAsync(artifactPath);

        Assert.Contains("## Denied actions", content, StringComparison.Ordinal);
        var body = content.Split("---", 3, StringSplitOptions.None)[2];
        var section = body[body.IndexOf("## Denied actions", StringComparison.Ordinal)..];
        Assert.Contains("write_file", section, StringComparison.Ordinal);
        Assert.Contains("../secrets.txt", section, StringComparison.Ordinal);
        Assert.Contains("/etc/secrets.txt", section, StringComparison.Ordinal);
        Assert.Contains("traversal", section, StringComparison.Ordinal);
        Assert.Contains("turn 2", section, StringComparison.Ordinal);

        // The narrative precedes the section and is preserved verbatim.
        Assert.Contains("Updated the topic page because the source refined it.", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("Updated the topic page", StringComparison.Ordinal) <
            body.IndexOf("## Denied actions", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompletedArtifact_WithoutDenials_HasNoDeniedActionsSection()
    {
        var root = Path.Combine(Path.GetTempPath(), $"artifact-body-{Guid.NewGuid():N}");
        var artifactPath = Path.Combine(root, "tasks", "task-artifact-1.md");

        var store = new TaskArtifactStore();
        await store.WriteAsync(artifactPath, CompletedDocument(deniedActions: []), CancellationToken.None);

        var content = await File.ReadAllTextAsync(artifactPath);

        Assert.DoesNotContain("## Denied actions", content, StringComparison.Ordinal);
    }
}
