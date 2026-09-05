namespace Grimoire.Hub.IngestSubmission;

public sealed record IngestSubmitSourceOptions(string Path, string SourceKind = "file", string? PastedText = null);
