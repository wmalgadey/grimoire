namespace Grimoire.Hub.Submission;

public sealed record SubmitSourceOptions(string Path, string SourceKind = "file", string? PastedText = null);
