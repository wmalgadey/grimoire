namespace Grimoire.Hub.IngestSubmission;

public sealed record SubmitSourceOptions(string Path, string SourceKind = "file", string? PastedText = null);
