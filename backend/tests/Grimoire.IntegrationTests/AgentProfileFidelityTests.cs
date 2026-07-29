using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.Domain.Guardrails;
using Grimoire.IngestAgent;
using Grimoire.IntegrationTests.Fakes;
using Grimoire.QueryAgent;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T025 (feature 010, SC-004/FR-004) — profile fidelity: an agent's effective
/// capabilities are exactly its profile's declared tool registry, enforced at the
/// guarded tool boundary. Cross-agent test (covers both agents, stays unprefixed per
/// the naming convention): (a) each host's registered tool set — the registry its
/// composition root passes to <c>GuardedToolExecutor</c>/<c>AgentLoop</c> and hence
/// the tool list offered to the model — is exactly its profile declaration; (b) a
/// scripted request for an out-of-profile tool is rejected at the guarded tool
/// boundary (never dispatched, zero writes) and the run continues.
/// </summary>
public class AgentProfileFidelityTests
{
    [Fact]
    public void IngestProfile_ToolSet_IsExactly_ListRead_Write()
    {
        Assert.Equal(
            ["list_files", "read_file", "write_file"],
            IngestToolRegistry.Default.Tools.Select(t => t.Name).ToArray());

        // The declarations are the shared, unchanged definitions (frozen schemas, FR-008).
        Assert.Equal(
            [ToolRegistry.ListFilesDefinition, ToolRegistry.ReadFileDefinition, ToolRegistry.WriteFileDefinition],
            IngestToolRegistry.Default.Tools);
    }

    [Fact]
    public void QueryProfile_ToolSet_IsExactly_ListRead_NoWriteTool()
    {
        Assert.Equal(
            ["list_files", "read_file"],
            QueryToolRegistry.Default.Tools.Select(t => t.Name).ToArray());

        Assert.Equal(
            [ToolRegistry.ListFilesDefinition, ToolRegistry.ReadFileDefinition],
            QueryToolRegistry.Default.Tools);

        Assert.DoesNotContain(QueryToolRegistry.Default.Tools, t => t.Name == "write_file");
    }

    [Fact]
    public async Task Query_OutOfProfileWriteRequest_IsRejectedAtTheGuardedBoundary_AndTheRunContinues()
    {
        var root = Path.Combine(Path.GetTempPath(), $"profile-fidelity-query-{Guid.NewGuid():N}");
        var pagesDir = Path.Combine(root, "pages");
        Directory.CreateDirectory(pagesDir);

        try
        {
            var policy = new SafetyPolicy(
                root,
                readPrefixes: [pagesDir + Path.DirectorySeparatorChar],
                writePrefixes: []);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(
                policy, journal, root, taskId: "turn-fidelity-1", registry: QueryToolRegistry.Default);

            // The model requests write_file — a tool the Query profile does not declare.
            var fakeModel = new FakeModelClient([
                FakeModelClient.WriteFileTurn("tool-1", "pages/new.md", "# should never be written"),
                FakeModelClient.FinalTurn("I cannot write; here is the answer instead.")]);

            var loop = new AgentLoop(fakeModel, executor, registry: QueryToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test query agent.",
                [new ConversationMessage("user", "Please write a page.")],
                "turn-fidelity-1",
                CancellationToken.None);

            // (a) the tool list offered to the model was exactly the profile declaration.
            Assert.All(fakeModel.Calls, call => Assert.Equal(
                ["list_files", "read_file"],
                call.Tools.Select(t => t.Name).ToArray()));

            // (b) the out-of-profile request was rejected at the boundary and the run
            // continued to a normal completion; nothing was written anywhere.
            Assert.Equal("I cannot write; here is the answer instead.", result.Narrative);
            Assert.Equal(2, fakeModel.CallCount);
            Assert.Empty(journal.JournaledPaths);
            Assert.Empty(executor.TouchedPaths);
            Assert.False(File.Exists(Path.Combine(pagesDir, "new.md")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Ingest_OutOfProfileToolRequest_IsRejectedAtTheGuardedBoundary_AndTheRunContinues()
    {
        var root = Path.Combine(Path.GetTempPath(), $"profile-fidelity-ingest-{Guid.NewGuid():N}");
        var pagesDir = Path.Combine(root, "pages");
        Directory.CreateDirectory(pagesDir);

        try
        {
            var policy = new SafetyPolicy(
                root,
                readPrefixes: [pagesDir + Path.DirectorySeparatorChar],
                writePrefixes: [pagesDir + Path.DirectorySeparatorChar]);

            var journal = new WriteJournal();
            var executor = new GuardedToolExecutor(
                policy, journal, root, taskId: "task-fidelity-1", registry: IngestToolRegistry.Default);

            // The model requests a tool no Grimoire profile declares at all.
            var fakeModel = new FakeModelClient([
                FakeModelClient.ToolCallTurn("tool-1", "delete_file", """{"path":"pages/adr.md"}"""),
                FakeModelClient.FinalTurn("Ingest finished without the unknown tool.")]);

            var loop = new AgentLoop(fakeModel, executor, registry: IngestToolRegistry.Default);

            var result = await loop.RunAsync(
                "You are a test ingest agent.",
                [new ConversationMessage("user", "Ingest this.")],
                "task-fidelity-1",
                CancellationToken.None);

            Assert.All(fakeModel.Calls, call => Assert.Equal(
                ["list_files", "read_file", "write_file"],
                call.Tools.Select(t => t.Name).ToArray()));

            Assert.Equal("Ingest finished without the unknown tool.", result.Narrative);
            Assert.Equal(2, fakeModel.CallCount);
            Assert.Empty(journal.JournaledPaths);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
