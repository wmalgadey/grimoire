namespace Grimoire.Hub.IngestSubmission;

public sealed record MarkItDownConversionResult(bool Success, string? Markdown, string? FailureReason);

/// <summary>
/// Seam between the ingest-submission pipeline and the document-to-Markdown converter
/// (ADR-010 P2): the single conversion entrypoint for PDF/Office documents and fetched
/// URL content. Markdown files are passed through as-is by the caller and never routed
/// through this port (FR-004).
/// </summary>
public interface IMarkdownConverter
{
    Task<MarkItDownConversionResult> ConvertAsync(string inputPath, CancellationToken cancellationToken = default);
}
