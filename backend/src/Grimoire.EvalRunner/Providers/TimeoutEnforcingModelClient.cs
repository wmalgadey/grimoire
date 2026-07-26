using Grimoire.IngestAgent.AgentCore;

namespace Grimoire.EvalRunner.Providers;

/// <summary>
/// Decorates any <see cref="IModelClient"/> (ADR-010 port) with a call-timeout bound
/// (007 FR-013, moved here by 009 — used for the runner's own model calls, i.e. the
/// capture-time LLM judge). Races the inner call against the timeout rather than relying
/// on the inner client to observe cancellation, so a hung call cannot block a run.
/// </summary>
public sealed class TimeoutEnforcingModelClient : IModelClient
{
    private readonly IModelClient _inner;
    private readonly TimeSpan _timeout;

    public TimeoutEnforcingModelClient(IModelClient inner, TimeSpan? timeout = null)
    {
        _inner = inner;
        _timeout = timeout ?? TimeSpan.FromSeconds(120);
    }

    public string ModelId => _inner.ModelId;

    public async Task<ModelTurn> NextTurnAsync(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> conversation,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        var innerTask = _inner.NextTurnAsync(systemPrompt, conversation, tools, cancellationToken);
        var timeoutTask = Task.Delay(_timeout, cancellationToken);

        var completed = await Task.WhenAny(innerTask, timeoutTask);
        if (completed == timeoutTask)
        {
            throw new ModelCallTimeoutException(_timeout);
        }

        return await innerTask;
    }
}

/// <summary>
/// Thrown when a single provider call exceeds its bound — distinct from a connectivity
/// failure and from an agent-judgment failure.
/// </summary>
public sealed class ModelCallTimeoutException : Exception
{
    public ModelCallTimeoutException(TimeSpan timeout)
        : base($"Model call exceeded the {timeout.TotalSeconds:0}s timeout.")
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}
