namespace Grimoire.LintAgent;

/// <summary>
/// CLI options for one message-turn agent process spawn (T042, 015-lint-board-parity,
/// ADR-018 "Message-turn mode"). Spawned by
/// <c>Grimoire.Hub.RemediationTasks.RemediationMessageTurnCoordinator.SubmitMessageTurnAsync</c>
/// exclusively. Proposal identity and attached context mirror
/// <see cref="RemediationExecutionCliOptions"/> exactly (same CLI surface, verbatim
/// agent-authored text, Principle V); the new human message and prior-turn context are
/// not CLI arguments — they are arbitrarily sized (a growing conversation), so they
/// travel on stdin as JSON instead (mirrors Grimoire.QueryAgent's conversation-input
/// convention, consistent with ADR-011's Query-turn shape), read separately by
/// <c>Program.cs</c>.
/// </summary>
public sealed record RemediationMessageTurnCliOptions(
    string TaskId,
    string RunId,
    string WikiRoot,
    string FoundationPromptPath,
    string SystemPromptPath,
    string PolicyPath,
    string WriteLocksDir,
    string ProposalTitle,
    string ProposalDescription,
    string? ProposalTargetPath,
    string? AttachedContext,
    int HeartbeatSeconds = 10);

/// <summary>
/// Stdin JSON payload for the message-turn mode: this turn's new human message plus
/// every prior message already in the task's Remediation Task Record (record-as-context,
/// R6) — mirrors <c>QueryConversationInput</c>'s prompt/priorTurns shape.
/// </summary>
public sealed record RemediationMessageTurnInput(string Message, IReadOnlyList<RemediationMessageTurnPriorMessage>? PriorMessages);

/// <summary>One prior human⇄agent exchange, as supplied on stdin.</summary>
public sealed record RemediationMessageTurnPriorMessage(string Sender, string Text);
