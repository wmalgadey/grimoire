using Grimoire.AgentRuntime.RunEvents;
using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.HarnessSurfaces;
using Grimoire.Hub.QueryConversations;
using Grimoire.Hub.QueryDispatch;
using Grimoire.IngestAgent.TaskArtifact;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T067 (022-align-wiki-structure, US3, ADR-023, FR-017/SC-011) — with a partial grant
/// (only <c>Findings</c> true), the effective grant set is correctly reconstructable from
/// each of the three records the contract names
/// (contracts/harness-surface-read-scope.md "Provenance"): the Ingest task artifact
/// frontmatter, the terminal NDJSON <c>completed</c> event, and the Query conversation
/// record's bookkeeping block. Each test exercises the REAL production write/parse code
/// (real filesystem I/O for the task artifact, the real NDJSON emitter + tolerant parser
/// for the terminal event, the real length-delimited record writer + parser for the
/// conversation record) — hermetic, no live LLM call (Constitution Principle II).
///
/// Also proves the Hub→CLI leg of the pipeline end to end: a partial
/// <see cref="HarnessSurfaceReadOptions"/> resolves, through the same
/// <see cref="HarnessSurfaceGrantResolver"/> every coordinator uses, to exactly
/// <c>["findings"]</c> — the value that then flows, unchanged, into all three records
/// below.
/// </summary>
public class HarnessSurfaceGrantProvenanceTests
{
    private static readonly IReadOnlyList<string> PartialGrant = ["findings"];

    [Fact]
    public void HarnessSurfaceGrantResolver_ResolvesPartialGrant_ToExactlyTheGrantedSurfaceName()
    {
        var options = new HarnessSurfaceReadOptions { Findings = true };

        var granted = HarnessSurfaceGrantResolver.ResolveGranted(options);

        Assert.Equal(PartialGrant, granted);
    }

    // ── Ingest task artifact frontmatter (TaskArtifactStore) ───────────────────────────

    [Fact]
    public async Task IngestTaskArtifact_RoundTrips_GrantedHarnessSurfaces()
    {
        var root = Path.Combine(Path.GetTempPath(), $"harness-surface-provenance-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var artifactPath = Path.Combine(root, "task-1.md");

        try
        {
            var store = new TaskArtifactStore();
            var document = new TaskArtifactDocument(
                TaskId: "task-1",
                Type: "ingest",
                Status: "completed",
                Agent: "ingest",
                StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAt: DateTimeOffset.UtcNow,
                SourceRef: "https://example.com/article",
                PagesTouched: [],
                FailureReason: null,
                Narrative: "Ingested one article.",
                GrantedHarnessSurfaces: PartialGrant);

            await store.WriteAsync(artifactPath, document, CancellationToken.None);
            var roundTripped = await store.ReadAsync(artifactPath, CancellationToken.None);

            // FR-017/SC-011: an operator reading the task artifact alone can reconstruct
            // exactly what the run was permitted to read.
            Assert.Equal(PartialGrant, roundTripped.GrantedHarnessSurfaces);

            var rawFrontmatter = await File.ReadAllTextAsync(artifactPath);
            Assert.Contains("granted_harness_surfaces: [\"findings\"]", rawFrontmatter);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task IngestTaskArtifact_WithNoGrant_RecordsAnExplicitEmptyList()
    {
        var root = Path.Combine(Path.GetTempPath(), $"harness-surface-provenance-artifact-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var artifactPath = Path.Combine(root, "task-2.md");

        try
        {
            var store = new TaskArtifactStore();
            var document = new TaskArtifactDocument(
                TaskId: "task-2",
                Type: "ingest",
                Status: "completed",
                Agent: "ingest",
                StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAt: DateTimeOffset.UtcNow,
                SourceRef: "https://example.com/article",
                PagesTouched: [],
                FailureReason: null,
                Narrative: "Ingested one article.",
                GrantedHarnessSurfaces: []);

            await store.WriteAsync(artifactPath, document, CancellationToken.None);
            var roundTripped = await store.ReadAsync(artifactPath, CancellationToken.None);

            Assert.Empty(roundTripped.GrantedHarnessSurfaces ?? []);

            var rawFrontmatter = await File.ReadAllTextAsync(artifactPath);
            Assert.Contains("granted_harness_surfaces: []", rawFrontmatter);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // ── Terminal NDJSON completed event (RunEventEmitter + AgentRunEventParser) ────────

    [Fact]
    public void TerminalNdjsonEvent_RoundTrips_GrantedHarnessSurfaces()
    {
        using var writer = new StringWriter();
        using (var emitter = new RunEventEmitter(writer, "turn-1"))
        {
            emitter.EmitCompleted(
                "Answered the question.",
                new RunCompletionMetadata(GrantedHarnessSurfaces: PartialGrant));
        }

        var line = writer.ToString().Trim();
        var parsed = AgentRunEventParser.TryParse(line);

        Assert.NotNull(parsed);
        Assert.Equal(PartialGrant, parsed!.GrantedHarnessSurfaces);
        // The pre-existing createdPages field is untouched by this Phase 5 addition
        // (Phase 6 renames it separately) — confirms the new field rides alongside it,
        // not in place of it.
        Assert.Contains("\"grantedHarnessSurfaces\":[\"findings\"]", line);
    }

    [Fact]
    public void TerminalNdjsonEvent_WithNoGrant_ParsesToNullOrEmpty()
    {
        using var writer = new StringWriter();
        using (var emitter = new RunEventEmitter(writer, "turn-2"))
        {
            emitter.EmitCompleted("Answered the question.", new RunCompletionMetadata());
        }

        var parsed = AgentRunEventParser.TryParse(writer.ToString().Trim());

        Assert.NotNull(parsed);
        Assert.True(parsed!.GrantedHarnessSurfaces is null or { Count: 0 });
    }

    // ── Query conversation record bookkeeping block (ConversationRecordFormat) ─────────

    [Fact]
    public void ConversationRecordBookkeeping_RoundTrips_GrantedHarnessSurfaces()
    {
        var turn = new RecordedTurn(
            TurnId: "turn-1",
            Position: 1,
            State: "completed",
            FailureReason: null,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt: DateTimeOffset.UtcNow,
            Model: "test-model",
            TurnsUsed: 2,
            InstructionFilePath: "agents/query/system-prompt.md",
            InstructionFileSha256: "deadbeef",
            PolicyPath: "agents/query/policy.json",
            PolicyVersion: 1,
            PolicySha256: "cafef00d",
            DeniedActions: [],
            Prompt: "What findings were reported recently?",
            Answer: "Here is a summary grounded in what I was permitted to read.",
            CreatedPages: [],
            GrantedHarnessSurfaces: PartialGrant);

        var header = ConversationRecordFormat.BuildRecordHeader("conv-1", DateTimeOffset.UtcNow);
        var block = ConversationRecordFormat.BuildTurnBlock(turn);

        Assert.Contains("granted_harness_surfaces:\n  - \"findings\"", block);

        var parseResult = ConversationRecordFormat.Parse(header + block);
        var parsed = Assert.IsType<ConversationRecordParseResult.Parsed>(parseResult);
        var parsedTurn = Assert.Single(parsed.Turns);

        // FR-017/SC-011: the conversation record alone reconstructs the effective scope.
        Assert.Equal(PartialGrant, parsedTurn.GrantedHarnessSurfacesOrEmpty);
    }

    [Fact]
    public void ConversationRecordBookkeeping_WithNoGrant_RecordsAnExplicitEmptyList()
    {
        var turn = new RecordedTurn(
            TurnId: "turn-2",
            Position: 1,
            State: "completed",
            FailureReason: null,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt: DateTimeOffset.UtcNow,
            Model: "test-model",
            TurnsUsed: 1,
            InstructionFilePath: "agents/query/system-prompt.md",
            InstructionFileSha256: "deadbeef",
            PolicyPath: "agents/query/policy.json",
            PolicyVersion: 1,
            PolicySha256: "cafef00d",
            DeniedActions: [],
            Prompt: "What does the wiki cover?",
            Answer: "A summary.",
            CreatedPages: [],
            GrantedHarnessSurfaces: []);

        var header = ConversationRecordFormat.BuildRecordHeader("conv-2", DateTimeOffset.UtcNow);
        var block = ConversationRecordFormat.BuildTurnBlock(turn);

        Assert.Contains("granted_harness_surfaces: []", block);

        var parseResult = ConversationRecordFormat.Parse(header + block);
        var parsed = Assert.IsType<ConversationRecordParseResult.Parsed>(parseResult);
        var parsedTurn = Assert.Single(parsed.Turns);

        Assert.Empty(parsedTurn.GrantedHarnessSurfacesOrEmpty);
    }
}
