using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Core.Adapters.Anthropic;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Instructions;
using Grimoire.QueryAgent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grimoire.AgentEvals;

/// <summary>One prior turn fed into a Query eval run (mirrors QueryConversationInput's shape).</summary>
public sealed record QueryEvalPriorTurn(string Prompt, string Answer);

/// <summary>Result of one Query agent eval run — no artifact is written (R3), so the
/// answer text and denial log are all the eval has to assert against.</summary>
public sealed record QueryEvalRunResult(
    string Answer,
    int TurnsUsed,
    IReadOnlyList<DeniedActionRecord> Denials);

/// <summary>
/// Drives one real Query agent turn against a fixture wiki (Fixtures/&lt;fixtureName&gt;/wiki),
/// using the same <see cref="AgentLoop"/>/<see cref="GuardedToolExecutor"/>/
/// <see cref="QueryToolRegistry"/> wiring as <c>Grimoire.QueryAgent/Program.cs</c>, minus the
/// process/CLI/NDJSON shell — mirrors <see cref="AgentEvalRunner"/>'s relationship to
/// <c>Grimoire.IngestAgent/Program.cs</c>. The Query agent never writes to the wiki, so unlike
/// the Ingest runner this never mutates the fixture directory.
/// </summary>
public sealed class QueryAgentEvalRunner
{
    private readonly string _repoRoot;
    private readonly string _fixturesRoot;
    private readonly ILogger<QueryAgentEvalRunner> _logger;

    public QueryAgentEvalRunner(ILogger<QueryAgentEvalRunner>? logger = null)
    {
        _repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        _fixturesRoot = Path.Combine(_repoRoot, "backend", "tests", "Grimoire.AgentEvals", "Fixtures");
        _logger = logger ?? NullLogger<QueryAgentEvalRunner>.Instance;
    }

    public async Task<QueryEvalRunResult> RunAsync(
        string fixtureName,
        string prompt,
        IReadOnlyList<QueryEvalPriorTurn> priorTurns,
        string runLabel,
        CancellationToken cancellationToken)
    {
        var wikiRoot = Path.Combine(_fixturesRoot, fixtureName, "wiki");
        var systemPromptPath = Path.Combine(_repoRoot, "data", "agents", "query", "system-prompt.md");
        var policyPath = Path.Combine(_repoRoot, "data", "agents", "query", "policy.json");

        var gateOutcome = EvalProviderResolver.Resolve();
        EvalObservability.RecordGateResolution(_logger, gateOutcome);
        if (gateOutcome.Status != EvalGateStatus.Enabled)
        {
            throw new InvalidOperationException(gateOutcome.Reason ?? "Eval provider is not configured.");
        }

        var configuration = gateOutcome.Configuration;

        var promptLoader = new SystemPromptLoader();
        var systemPromptResult = await promptLoader.LoadAsync(systemPromptPath, cancellationToken);
        if (systemPromptResult.IsSecond(out var systemPromptFailure))
        {
            throw new InvalidOperationException($"Query system prompt invalid for eval run: {systemPromptFailure.Reason}");
        }
        systemPromptResult.IsFirst(out var systemPromptDoc);

        var policyLoader = new PolicyLoader(wikiRoot);
        var policyResult = await policyLoader.LoadAsync(policyPath, cancellationToken);
        if (policyResult.IsSecond(out var policyFailure))
        {
            throw new InvalidOperationException($"Query policy invalid for eval run: {policyFailure.Reason}");
        }
        policyResult.IsFirst(out var loadedPolicy);

        var taskId = $"eval-{runLabel}-{Guid.NewGuid():N}";
        var anthropicModelClient = AgentEvalRunner.CreateProviderWiredAnthropicClient(configuration);
        var recordingModelClient = new RecordingModelClient(new TimeoutEnforcingModelClient(anthropicModelClient));

        var journal = new WriteJournal();
        var executor = new GuardedToolExecutor(
            loadedPolicy!.Policy,
            journal,
            wikiRoot,
            taskId: taskId,
            registry: QueryToolRegistry.Default);

        var loop = new AgentLoop(recordingModelClient, executor, registry: QueryToolRegistry.Default);

        var conversation = new List<ConversationMessage>();
        foreach (var prior in priorTurns)
        {
            conversation.Add(new ConversationMessage("user", prior.Prompt));
            if (!string.IsNullOrEmpty(prior.Answer))
            {
                conversation.Add(new ConversationMessage("assistant", prior.Answer));
            }
        }
        conversation.Add(new ConversationMessage("user", prompt));

        try
        {
            var result = await loop.RunAsync(systemPromptDoc!.Content, conversation, taskId, cancellationToken);
            return new QueryEvalRunResult(result.Narrative, result.TurnsUsed, executor.Denials);
        }
        catch (Exception ex) when (ex is ModelCallTimeoutException timeoutEx)
        {
            EvalObservability.RecordSampleTimeout(
                _logger, runLabel, EvalObservability.ProviderLabel(configuration.Kind), configuration.Model, timeoutEx.Timeout.TotalSeconds);
            throw;
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
