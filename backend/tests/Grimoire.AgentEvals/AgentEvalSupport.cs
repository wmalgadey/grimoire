using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grimoire.IngestAgent;
using Grimoire.IngestAgent.AgentCore;
using Grimoire.IngestAgent.Guardrails;
using Grimoire.IngestAgent.IngestLog;
using Grimoire.IngestAgent.TaskArtifact;

namespace Grimoire.AgentEvals;

public sealed class EvalFactAttribute : FactAttribute
{
    public EvalFactAttribute()
    {
        if (!EvalGate.IsEnabled)
        {
            Skip = EvalGate.SkipReason;
        }
    }
}

public static class EvalGate
{
    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("GRIMOIRE_EVAL"), "1", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN"));

    public static string SkipReason =>
        "Set GRIMOIRE_EVAL=1 and ANTHROPIC_AUTH_TOKEN to run agent-behavior evals.";

    public static int ResolveSampleCount()
    {
        var raw = Environment.GetEnvironmentVariable("GRIMOIRE_EVAL_SAMPLES");
        if (!int.TryParse(raw, out var value))
        {
            return 10;
        }

        return Math.Clamp(value, 1, 20);
    }
}

public sealed record EvalRunResult(
    string TaskId,
    string Status,
    TaskArtifactDocument Artifact,
    string SandboxRoot,
    string TranscriptPath,
    IReadOnlyList<string> PageFiles,
    string IndexContent);

public sealed class AgentEvalRunner
{
    private readonly string _repoRoot;
    private readonly string _fixturesRoot;
    private readonly string _transcriptRoot;

    public AgentEvalRunner()
    {
        _repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        _fixturesRoot = Path.Combine(_repoRoot, "backend", "tests", "Grimoire.AgentEvals", "Fixtures");
        _transcriptRoot = Path.Combine(Path.GetTempPath(), "grimoire-agent-evals", "transcripts");
        Directory.CreateDirectory(_transcriptRoot);
    }

    public async Task<EvalRunResult> RunAsync(
        string fixtureName,
        string sourceContent,
        string runLabel,
        Action<string>? mutateSkillFile,
        CancellationToken cancellationToken)
    {
        var taskId = $"eval-{runLabel}-{Guid.NewGuid():N}";
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "grimoire-agent-evals", taskId);
        var fixtureRoot = Path.Combine(_fixturesRoot, fixtureName);
        var wikiFixtureRoot = Path.Combine(fixtureRoot, "wiki");

        CopyDirectory(wikiFixtureRoot, Path.Combine(sandboxRoot, "wiki"));
        CopyDirectory(Path.Combine(_repoRoot, "agents", "ingest"), Path.Combine(sandboxRoot, "agents", "ingest"));

        if (mutateSkillFile is not null)
        {
            var skillPath = Path.Combine(sandboxRoot, "agents", "ingest", "skills", "wiki-maintenance", "SKILL.md");
            mutateSkillFile(skillPath);
        }

        var options = new AgentCliOptions(
            TaskId: taskId,
            SourceRef: $"eval://{fixtureName}/{runLabel}",
            SourceKind: "pasted_text",
            PagesDir: Path.Combine(sandboxRoot, "wiki", "pages"),
            TasksDir: Path.Combine(sandboxRoot, "wiki", "tasks"),
            IndexPath: Path.Combine(sandboxRoot, "wiki", "index.md"),
            LogPath: Path.Combine(sandboxRoot, "wiki", "log.md"),
            PastedText: sourceContent,
            InstructionsDir: Path.Combine(sandboxRoot, "agents", "ingest"),
            PolicyPath: Path.Combine(sandboxRoot, "agents", "ingest", "policy.json"));

        Directory.CreateDirectory(options.PagesDir);
        Directory.CreateDirectory(options.TasksDir);

        var taskStore = new TaskArtifactStore();
        var logAppender = new IngestLogAppender();
        var startTime = DateTimeOffset.UtcNow;

        await taskStore.WriteAsync(
            options.TaskArtifactPath,
            new TaskArtifactDocument(
                TaskId: options.TaskId,
                Type: "ingest",
                Status: "running",
                Agent: "ingest",
                StartedAt: startTime,
                CompletedAt: null,
                SourceRef: options.SourceRef,
                PagesTouched: [],
                FailureReason: null,
                Narrative: "Eval run started."),
            cancellationToken);

        var recordingModelClient = new RecordingModelClient(new AnthropicModelClient());
        var instructionLoader = new InstructionSetLoader();
        var instructionResult = await instructionLoader.LoadAsync(options.InstructionsDir, cancellationToken);
        if (instructionResult.IsSecond(out var instructionFailure))
        {
            throw new InvalidOperationException($"Instruction set invalid for eval run: {instructionFailure.Reason}");
        }

        instructionResult.IsFirst(out var instructionSet);

        var policyLoader = new PolicyLoader(sandboxRoot);
        var policyResult = await policyLoader.LoadAsync(options.PolicyPath, cancellationToken);
        if (policyResult.IsSecond(out var policyFailure))
        {
            throw new InvalidOperationException($"Policy invalid for eval run: {policyFailure.Reason}");
        }

        policyResult.IsFirst(out var loadedPolicy);

        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(loadedPolicy!.Policy, journal, sandboxRoot);
        var loop = new AgentLoop(recordingModelClient, executor);

        try
        {
            var loopResult = await loop.RunAsync(
                instructionSet!.BuildSystemPrompt(),
                options.TaskId,
                options.SourceRef,
                options.PastedText ?? string.Empty,
                cancellationToken);

            var touched = executor.TouchedPaths.Select(p => Path.GetRelativePath(sandboxRoot, p)).ToList();
            var doc = new TaskArtifactDocument(
                TaskId: options.TaskId,
                Type: "ingest",
                Status: "completed",
                Agent: "ingest",
                StartedAt: startTime,
                CompletedAt: DateTimeOffset.UtcNow,
                SourceRef: options.SourceRef,
                PagesTouched: touched,
                FailureReason: null,
                Narrative: loopResult.Narrative,
                PagesCreated: touched,
                PagesUpdated: [],
                PagesSuperseded: [],
                DeniedActions: executor.Denials.Select(d => new DeniedActionEntry(d.Action, d.Target, d.Reason, d.Turn)).ToList(),
                InstructionFiles: instructionSet.Files.Select(f => new InstructionFileRecord(f.Path, f.Sha256)).ToList(),
                Policy: new PolicyRecord(loadedPolicy.Identity.Path, loadedPolicy.Identity.Version, loadedPolicy.Identity.Sha256),
                Model: recordingModelClient.ModelId,
                Turns: loopResult.TurnsUsed,
                RolledBack: null);

            await taskStore.WriteAsync(options.TaskArtifactPath, doc, cancellationToken);
            await logAppender.EnsureLogEntryAsync(options.LogPath, "completed", options.SourceRef, options.TaskId, false, cancellationToken);

            var transcriptPath = await WriteTranscriptAsync(taskId, recordingModelClient, cancellationToken);
            var artifact = await taskStore.ReadAsync(options.TaskArtifactPath, cancellationToken);

            return new EvalRunResult(
                taskId,
                artifact.Status,
                artifact,
                sandboxRoot,
                transcriptPath,
                GetPageFiles(sandboxRoot),
                ReadIfExists(options.IndexPath));
        }
        catch (Exception ex)
        {
            var rollback = await journal.RollbackAsync(cancellationToken);
            var doc = new TaskArtifactDocument(
                TaskId: options.TaskId,
                Type: "ingest",
                Status: "failed",
                Agent: "ingest",
                StartedAt: startTime,
                CompletedAt: DateTimeOffset.UtcNow,
                SourceRef: options.SourceRef,
                PagesTouched: [],
                FailureReason: ex.Message,
                Narrative: $"Eval run failed: {ex.Message}",
                PagesCreated: [],
                PagesUpdated: [],
                PagesSuperseded: [],
                DeniedActions: executor.Denials.Select(d => new DeniedActionEntry(d.Action, d.Target, d.Reason, d.Turn)).ToList(),
                InstructionFiles: instructionSet!.Files.Select(f => new InstructionFileRecord(f.Path, f.Sha256)).ToList(),
                Policy: new PolicyRecord(loadedPolicy!.Identity.Path, loadedPolicy.Identity.Version, loadedPolicy.Identity.Sha256),
                Model: recordingModelClient.ModelId,
                Turns: null,
                RolledBack: rollback.Values.All(v => v));

            await taskStore.WriteAsync(options.TaskArtifactPath, doc, cancellationToken);
            await logAppender.EnsureLogEntryAsync(options.LogPath, "failed", options.SourceRef, options.TaskId, true, cancellationToken);

            var transcriptPath = await WriteTranscriptAsync(taskId, recordingModelClient, cancellationToken);
            var artifact = await taskStore.ReadAsync(options.TaskArtifactPath, cancellationToken);

            return new EvalRunResult(
                taskId,
                artifact.Status,
                artifact,
                sandboxRoot,
                transcriptPath,
                GetPageFiles(sandboxRoot),
                ReadIfExists(options.IndexPath));
        }
    }

    private async Task<string> WriteTranscriptAsync(
        string taskId,
        RecordingModelClient recordingModelClient,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_transcriptRoot, $"{taskId}.json");
        var payload = JsonSerializer.Serialize(recordingModelClient.Calls, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, payload, cancellationToken);
        return path;
    }

    private static string[] GetPageFiles(string sandboxRoot)
    {
        var pagesDir = Path.Combine(sandboxRoot, "wiki", "pages");
        if (!Directory.Exists(pagesDir))
        {
            return [];
        }

        return Directory.GetFiles(pagesDir, "*.md", SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ReadIfExists(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destinationDir, fileName), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(directory);
            CopyDirectory(directory, Path.Combine(destinationDir, dirName));
        }
    }

    private static string FindRepoRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            var hasGit = Directory.Exists(Path.Combine(current.FullName, ".git"));
            var hasSpecify = Directory.Exists(Path.Combine(current.FullName, ".specify"));
            if (hasGit || hasSpecify)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root for eval tests.");
    }
}

public sealed class RecordingModelClient : IModelClient
{
    private readonly IModelClient _inner;
    private int _turn;

    public RecordingModelClient(IModelClient inner)
    {
        _inner = inner;
    }

    public string ModelId => _inner.ModelId;

    public List<RecordedEvalTurn> Calls { get; } = [];

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var turn = await _inner.NextTurnAsync(systemPrompt, conversation, tools, cancellationToken);
        _turn++;

        Calls.Add(new RecordedEvalTurn(
            _turn,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(systemPrompt))),
            conversation.Select(c => new RecordedMessage(c.Role, c.Content)).ToList(),
            tools.Select(t => t.Name).ToList(),
            turn.StopReason,
            turn.ToolUseRequests.Select(t => new RecordedToolUse(t.ToolUseId, t.ToolName, t.InputJson)).ToList(),
            turn.AssistantText,
            turn.InputTokens,
            turn.OutputTokens));

        return turn;
    }
}

public sealed record RecordedEvalTurn(
    int Turn,
    string SystemPromptSha256,
    IReadOnlyList<RecordedMessage> Conversation,
    IReadOnlyList<string> ToolNames,
    ModelStopReason StopReason,
    IReadOnlyList<RecordedToolUse> ToolUses,
    string? AssistantText,
    int InputTokens,
    int OutputTokens);

public sealed record RecordedMessage(string Role, string Content);

public sealed record RecordedToolUse(string ToolUseId, string ToolName, string InputJson);
