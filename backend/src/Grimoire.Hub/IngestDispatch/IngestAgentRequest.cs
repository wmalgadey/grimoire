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
    string? UserPrompt = null);
