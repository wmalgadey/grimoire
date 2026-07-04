using Grimoire.IngestAgent.TaskArtifact;

namespace Grimoire.IntegrationTests;

public class TaskArtifactDeterminismTests
{
    [Fact]
    public async Task ArtifactWriter_ProducesStablePathListsAndDeniedActionShape()
    {
        var root = Path.Combine(Path.GetTempPath(), $"artifact-determinism-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, "task.md");
        var doc = new TaskArtifactDocument(
            TaskId: "task-1",
            Operation: "ingest",
            Status: "completed",
            StartedAt: DateTimeOffset.Parse("2026-07-04T00:00:00Z"),
            FinishedAt: DateTimeOffset.Parse("2026-07-04T00:01:00Z"),
            SourceRef: "docs/source.md",
            CreatedPaths: ["pages/a.md"],
            UpdatedPaths: ["pages/b.md"],
            SupersededPaths: ["pages/c.md"],
            DeniedActions: [new DeniedActionRecord("write", "outside.md", "Denied by default policy.")],
            UserQuestions: ["Should page C be superseded?"],
            Summary: "Completed",
            FailureReason: null,
            InstructionContext: new InstructionContextRecord("CLAUDE.md", ["SKILL.md"], "hash"));

        var store = new TaskArtifactStore();
        await store.WriteAsync(path, doc, CancellationToken.None);
        var reloaded = await store.ReadAsync(path, CancellationToken.None);

        Assert.Equal(doc.CreatedPaths, reloaded.CreatedPaths);
        Assert.Equal(doc.UpdatedPaths, reloaded.UpdatedPaths);
        Assert.Equal(doc.SupersededPaths, reloaded.SupersededPaths);
        Assert.Single(reloaded.DeniedActions);
        Assert.Equal("write", reloaded.DeniedActions[0].Action);
    }
}
