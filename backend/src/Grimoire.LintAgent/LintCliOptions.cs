namespace Grimoire.LintAgent;

/// <summary>
/// CLI options for one Lint Run's process spawn (ADR-002 pattern, data-model.md Lint
/// Run). Unlike Ingest/Query, Lint takes no per-run user input at all (no pasted source,
/// no conversation prompt) — the whole wiki is its input, read via <c>list_files</c>/
/// <c>read_file</c> once the run starts. No default-user-prompt document either
/// (<c>RequiredInstructionDocuments = { SystemPrompt }</c>, same as Query).
/// </summary>
public sealed record LintCliOptions(
    string RunId,
    string WikiRoot,
    string SystemPromptPath,
    string PolicyPath,
    // ADR-015 (012-query-synthesis-writes), extended by ADR-016 (013-lint-agent):
    // required, mirroring --wiki-root/--policy-path (Lint's frontmatter-only writes
    // reuse the same cross-process write-coordination lock unchanged).
    string WriteLocksDir,
    int HeartbeatSeconds = 10,
    // T036 (013-lint-agent, US2): the Hub-computed effective Review Window (days,
    // Grimoire:LintReviewWindowDays, default 90) — threaded into the kickoff message so
    // the agent's own stated default (data/agents/lint/system-prompt.md) can be
    // overridden without an instruction-file edit.
    int ReviewWindowDays = 90,
    // ADR-023 (022-align-wiki-structure, Phase 5): the ordered list of reserved
    // harness-surface names this run's operator has granted (empty = none granted).
    IReadOnlyList<string>? GrantedHarnessSurfaces = null);
