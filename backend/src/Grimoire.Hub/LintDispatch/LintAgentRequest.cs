namespace Grimoire.Hub.LintDispatch;

/// <summary>
/// One Lint Run's process-spawn request (data-model.md Lint Run, ADR-016). Flows through
/// the same <see cref="Grimoire.Hub.AgentDispatch.IAgentProcessLauncher"/> port Ingest and
/// Query use, via a dedicated <c>StartAsync</c> overload — the port itself is unchanged.
/// Unlike Query, there is no per-run prompt/prior-turn payload at all: Lint takes no
/// input beyond the wiki itself, so nothing is written to the spawned process's stdin.
/// </summary>
public sealed record LintAgentRequest(
    string RunId,
    string WikiRoot,
    string SystemPromptPath,
    string PolicyPath,
    // ADR-015 (012-query-synthesis-writes), extended by ADR-016 (013-lint-agent): the
    // cross-process write-coordination lock directory, supplied the same way as
    // WikiRoot/PolicyPath — a single Hub-resolved composition point
    // (ResolvedGrimoirePaths.WriteLocksDir, ADR-009), not agent-discovered.
    string WriteLocksDir,
    // T036 (013-lint-agent, US2): the effective Review Window (days), sourced from
    // LintReviewWindowOptions (Grimoire:LintReviewWindowDays, default 90) — threaded into
    // the spawned process's kickoff context so the agent's own default (also 90, stated
    // in data/agents/lint/system-prompt.md) can be overridden without an instruction-file
    // edit. Optional/defaulted so every pre-existing positional call site keeps compiling.
    int ReviewWindowDays = 90);
