namespace Grimoire.Hub.AgentDispatch;

public sealed record IngestAgentRequest(
    string TaskId,
    string SourceRef,
    string SourceKind,
    string PagesDir,
    string TasksDir,
    string IndexPath,
    string LogPath,
    string? PastedText);
