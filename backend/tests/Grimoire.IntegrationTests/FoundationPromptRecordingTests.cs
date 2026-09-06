using Grimoire.Hub.AgentDispatch;
using Grimoire.Hub.AgentDispatch.Adapters.AgentProcess;
using Grimoire.Hub.IngestDispatch;
using Grimoire.Hub.LintDispatch;
using Grimoire.Hub.LintFindings;
using Grimoire.Hub.OperationalState;
using Grimoire.Hub.RemediationTasks;
using Grimoire.IngestAgent.TaskArtifact;
using Grimoire.IntegrationTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.IntegrationTests;

/// <summary>
/// T024 (029-shared-foundation-prompt, US1, FR-006/SC-001): dispatching each of the three
/// agent types records **both** the foundation document and the role document, each with
/// its own path and hash — never one merged entry, never one silently dropped. One sub-test
/// per agent type, since each records this through a structurally different mechanism
/// (Ingest: task artifact <c>instruction_files</c> list; Query: Conversation Record
/// <c>foundation_file</c>/<c>instruction_file</c> blocks; Lint: Findings Report same blocks).
/// </summary>
public class FoundationPromptRecordingTests
{
    [Fact]
    public async Task Ingest_RecordsBothDocuments_WithDistinctPathsAndHashes_EvenOnAPreModelFailure()
    {
        // A real spawned Ingest process, driven to a source-read failure — which happens
        // inside ExecuteAsync, strictly after OnInstructionsLoadedAsync has already stored
        // both loaded documents — so DescribeUnhandledFailureAsync's fail-closed artifact
        // still names both (Program.cs's own instructions field, now threaded through).
        // Mirrors IngestFailureAndReconciliationTests's missing-source-file technique, but
        // needs a syntactically valid (never dialed) model id so PrepareAsync's
        // ModelClientFactory.Create succeeds and the run reaches ExecuteAsync at all — this
        // sandboxed test environment has no real model credentials configured.
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-foundation-ingest-{Guid.NewGuid():N}");
        var tasksDir = Path.Combine(root, "tasks");
        var indexPath = Path.Combine(root, "index.md");
        var logPath = Path.Combine(root, "log.md");
        Directory.CreateDirectory(tasksDir);
        await File.WriteAllTextAsync(logPath, string.Empty);

        var instructionsDir = Path.Combine(root, "instructions");
        Directory.CreateDirectory(instructionsDir);
        var foundationPromptPath = Path.Combine(instructionsDir, "foundation-prompt.md");
        var systemPromptPath = Path.Combine(instructionsDir, "system-prompt.md");
        var defaultUserPromptPath = Path.Combine(instructionsDir, "default-user-prompt.md");
        var policyPath = Path.Combine(instructionsDir, "policy.json");
        await File.WriteAllTextAsync(foundationPromptPath, "# Foundation\nEvery agent shares this.\n");
        await File.WriteAllTextAsync(systemPromptPath, "# Role\nOnly ingest does this.\n");
        await File.WriteAllTextAsync(defaultUserPromptPath, "Integrate the source.");
        await File.WriteAllTextAsync(policyPath, """
            {
              "version": 1,
              "defaultDecision": "deny",
              "read": [{"pathPrefix": "."}],
              "write": [{"pathPrefix": "."}]
            }
            """);

        var previousModelEnvVar = Environment.GetEnvironmentVariable("GRIMOIRE_INGEST_MODEL");
        try
        {
            // Never dialed: ExecuteAsync throws on the missing source file before the
            // loop ever calls the model, so this value is read but never used to make a
            // network request (AnthropicModelClient's constructor performs no live call).
            Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_MODEL", "test-model-029");

            var loader = new LocalSecretsLoader(Path.Combine(root, ".env"));
            var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
            var agentDir = Path.Combine(repoRoot, ".grimoire", "agents");
            var agentWorkerPath = Path.Combine(agentDir, "ingest", "Grimoire.IngestAgent.dll");
            var queryAgentWorkerPath = Path.Combine(agentDir, "query", "Grimoire.QueryAgent.dll");
            var lintAgentWorkerPath = Path.Combine(agentDir, "lint", "Grimoire.LintAgent.dll");
            var processHost = new AgentProcessHost(loader, agentWorkerPath, queryAgentWorkerPath, lintAgentWorkerPath);

            var taskId = $"test-{Guid.NewGuid():N}";
            var exitCode = await processHost.RunToExitAsync(new IngestAgentRequest(
                TaskId: taskId,
                SourceRef: Path.Combine(root, "missing-source.md"),
                SourceKind: "file",
                WikiRoot: root,
                ContentRoot: root,
                TasksDir: tasksDir,
                IndexPath: indexPath,
                LogPath: logPath,
                PastedText: null,
                FoundationPromptPath: foundationPromptPath,
                SystemPromptPath: systemPromptPath,
                DefaultUserPromptPath: defaultUserPromptPath,
                PolicyPath: policyPath,
                WriteLocksDir: Path.Combine(root, "write-locks")));

            Assert.Equal(1, exitCode);

            var doc = await new TaskArtifactStore().ReadAsync(Path.Combine(tasksDir, $"{taskId}.md"));
            Assert.NotNull(doc.InstructionFiles);
            Assert.Equal(2, doc.InstructionFiles!.Count);

            var foundationEntry = doc.InstructionFiles[0];
            var roleEntry = doc.InstructionFiles[1];
            Assert.Equal(foundationPromptPath, foundationEntry.Path);
            Assert.Equal(systemPromptPath, roleEntry.Path);
            Assert.False(string.IsNullOrWhiteSpace(foundationEntry.Sha256));
            Assert.False(string.IsNullOrWhiteSpace(roleEntry.Sha256));
            Assert.NotEqual(foundationEntry.Sha256, roleEntry.Sha256);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GRIMOIRE_INGEST_MODEL", previousModelEnvVar);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Query_RecordsBothDocuments_WithDistinctPathsAndHashes()
    {
        var launcher = new FakeAgentProcessLauncher(autoPlay: true)
        {
            ScriptedAnswerChunks = [("The wiki says so.", TimeSpan.Zero)],
            ScriptedQueryTerminalMetadata = new Dictionary<string, object?>
            {
                ["systemPromptSha256"] = "role-sha-query-1",
                ["foundationPromptSha256"] = "foundation-sha-query-1",
            },
        };
        using var harness = await HubCliQueryTestHarness.CreateAsync(launcher);
        const string conversationId = "2026-09-05-query-foundation-recording";

        var (exitCode, _, _) = await harness.RunQueryCommandAsync("What does the wiki say?", conversationId);
        Assert.Equal(0, exitCode);

        var recordPath = harness.Paths.ConversationRecordPathFor(conversationId);
        var record = await File.ReadAllTextAsync(recordPath);

        Assert.Contains("foundation_file:\n  path: \"agents/query/foundation-prompt.md\"\n  sha256: \"foundation-sha-query-1\"\n", record, StringComparison.Ordinal);
        Assert.Contains("instruction_file:\n  path: \"agents/query/system-prompt.md\"\n  sha256: \"role-sha-query-1\"\n", record, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lint_RecordsBothDocuments_WithDistinctPathsAndHashes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"grimoire-foundation-lint-{Guid.NewGuid():N}");
        var paths = QueryTurnSubmissionApiTests.BuildResolvedPaths(root);
        Directory.CreateDirectory(paths.FindingsDir);

        try
        {
            var repository = new OperationalStateRepository(paths.StateDbPath);
            await repository.InitializeAsync();

            var launcher = new FakeAgentProcessLauncher(autoPlay: true)
            {
                ScriptedLintTerminalMetadata = new Dictionary<string, object?>
                {
                    ["systemPromptSha256"] = "role-sha-lint-1",
                    ["foundationPromptSha256"] = "foundation-sha-lint-1",
                },
            };

            var coordinator = new LintRunCoordinator(
                launcher,
                new LintFindingsReportStore(paths, NullLogger<LintFindingsReportStore>.Instance),
                paths,
                logger: NullLogger<LintRunCoordinator>.Instance,
                stateRepository: repository,
                remediationRecordStore: new RemediationTaskRecordStore(paths));

            var result = await coordinator.TriggerAsync();
            var accepted = Assert.IsType<LintSubmissionResult.Accepted>(result);
            var runId = accepted.Run.RunId;

            await PollAsync.WaitAsync(
                () => coordinator.GetRun(runId) is { IsTerminal: true } run && run.FindingsReportPath is not null,
                TimeSpan.FromSeconds(10),
                $"Expected lint run '{runId}' to reach a terminal status with a Findings Report within 10s.");

            var reportStore = new LintFindingsReportStore(paths, NullLogger<LintFindingsReportStore>.Instance);
            var report = await reportStore.TryReadAsync(runId);
            Assert.NotNull(report);

            Assert.Contains("foundation_file:\n  path: \"agents/lint/foundation-prompt.md\"\n  sha256: \"foundation-sha-lint-1\"\n", report, StringComparison.Ordinal);
            Assert.Contains("instruction_file:\n  path: \"agents/lint/system-prompt.md\"\n  sha256: \"role-sha-lint-1\"\n", report, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
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
