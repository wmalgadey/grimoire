using Grimoire.Hub.AgentDispatch;
namespace Grimoire.Hub.IngestDispatch;

public sealed record IngestAgentRequest(
    string TaskId,
    string SourceRef,
    string SourceKind,
    string WikiRoot,
    string ContentRoot,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string? PastedText,
    string SystemPromptPath,
    string DefaultUserPromptPath,
    string PolicyPath,
    string WriteLocksDir,
    string? UserPrompt = null,
    // ADR-023 (022-align-wiki-structure, Phase 5): the effective granted-surface list for
    // this run (Grimoire:HarnessSurfaceReads), threaded to AgentProcessHost's
    // --granted-harness-surfaces spawn argument. Empty means none granted (deny-by-default).
    IReadOnlyList<string>? GrantedHarnessSurfaces = null);
