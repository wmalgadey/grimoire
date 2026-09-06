using Grimoire.AgentRuntime.Core;
using Grimoire.AgentRuntime.Guardrails;
using Grimoire.AgentRuntime.Host;
using Grimoire.Domain.Guardrails;

namespace Grimoire.IntegrationTests.Fakes;

/// <summary>
/// 029-shared-foundation-prompt (T025/T027/T028): a hand-rolled <see cref="IAgentIntentHandler"/>
/// test double — mirroring the existing public pattern in
/// <c>LintPolicyIdentityTests.NeverExecuteIntentHandler</c> — used because none of the three
/// real agent projects exposes its own <c>Program.cs</c>-internal intent handler to this test
/// assembly. Captures <see cref="LoadedInstructions"/> as soon as it loads, then — mirroring
/// what every real Program.cs actually does — hands the composed prompt to a real
/// <see cref="AgentLoop"/> backed by the scripted <see cref="FakeModelClient"/> port fake via
/// the agent-agnostic <c>AgentLoop.RunAsync</c> entry point (the same one Query and Lint call
/// directly, and that Ingest's own <c>RunIngestSourceAsync</c> delegates to).
/// </summary>
internal sealed class CapturingIntentHandler(string wikiRoot, FakeModelClient modelClient, ToolRegistry toolRegistry)
    : IAgentIntentHandler
{
    public LoadedInstructions? Instructions { get; private set; }
    public string? FailedDocumentKind { get; private set; }
    public string? FailureReason { get; private set; }
    public bool ExecuteAsyncWasCalled { get; private set; }

    public Task PrepareAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OnInstructionLoadFailureAsync(
        string documentKind, string documentPath, string reason, CancellationToken cancellationToken)
    {
        FailedDocumentKind = documentKind;
        FailureReason = reason;
        return Task.CompletedTask;
    }

    public Task OnInstructionsLoadedAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        Instructions = instructions;
        return Task.CompletedTask;
    }

    public async Task<int> ExecuteAsync(LoadedInstructions instructions, CancellationToken cancellationToken)
    {
        ExecuteAsyncWasCalled = true;
        var policy = new SafetyPolicy(wikiRoot, readPrefixes: [], writePrefixes: []);
        var executor = new GuardedToolExecutor(policy, new WriteJournal(), wikiRoot, registry: toolRegistry);
        var loop = new AgentLoop(modelClient, executor, registry: toolRegistry);

        var initialConversation = new List<ConversationMessage>
        {
            new("user", [new ConversationTextBlock("Test message.")]),
        };
        await loop.RunAsync(instructions.ComposedSystemPrompt, initialConversation, "task-composition-1", cancellationToken);
        return 0;
    }

    public Task<string> DescribeUnhandledFailureAsync(Exception exception, CancellationToken cancellationToken)
        => Task.FromResult(exception.Message);
}
