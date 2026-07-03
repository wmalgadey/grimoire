namespace Grimoire.Hub.AgentDispatch;

public sealed record IngestAgentRequest(
    string TaskId,
    string SourceRef,
    string SourceKind,
    string WikiDir,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string? PastedText);
