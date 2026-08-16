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
    // 023 T046 (FR-003): the Hub-resolved human-readable label, handed to the agent as an
    // explicit launch input so the agent's own artifact writes carry it forward instead of
    // dropping it. Each process keeps its own artifact I/O (ADR-002) — no read-modify-write
    // on the shared file.
    string? Title = null);
