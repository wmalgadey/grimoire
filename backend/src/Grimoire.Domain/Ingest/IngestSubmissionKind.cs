namespace Grimoire.Domain.Ingest;

/// <summary>
/// Source kind for a single ingest submission (data-model.md IngestSubmission, FR-001).
/// </summary>
public enum IngestSubmissionKind
{
    Url,
    MarkdownFile,
    PdfFile,
    OfficeFile,
}
