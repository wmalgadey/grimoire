using Grimoire.Domain.Ingest;

namespace Grimoire.Hub.IngestSubmission;

/// <summary>
/// 400: malformed/missing required field. 415: recognizable but unsupported file format.
/// 422: correct shape but a precondition fails immediately (empty file, malformed URL).
/// </summary>
public enum IngestSubmissionValidationErrorKind
{
    None,
    BadRequest,
    UnsupportedMediaType,
    UnprocessableEntity,
}

public sealed record IngestSubmissionValidationResult(bool IsValid, string? ErrorMessage, IngestSubmissionValidationErrorKind ErrorKind = IngestSubmissionValidationErrorKind.None)
{
    public static readonly IngestSubmissionValidationResult Valid = new(true, null);
}

/// <summary>
/// Validates an ingest submission before a Task Artifact is created (FR-001, FR-003): exactly
/// one source per submission, of a supported kind. Office documents are the bounded set named in
/// spec.md Assumptions (Word/PowerPoint/Excel).
/// </summary>
public sealed class IngestSubmissionValidator
{
    private static readonly HashSet<string> _markdownExtensions = new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown" };
    private static readonly HashSet<string> _officeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx",
    };

    public IngestSubmissionValidationResult ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new IngestSubmissionValidationResult(false, "A URL is required for a url submission.", IngestSubmissionValidationErrorKind.BadRequest);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new IngestSubmissionValidationResult(false, "The submitted URL must be an absolute http(s) URL.", IngestSubmissionValidationErrorKind.UnprocessableEntity);
        }

        return IngestSubmissionValidationResult.Valid;
    }

    public IngestSubmissionValidationResult ValidateFile(IngestSubmissionKind kind, string fileName, long sizeBytes)
    {
        var extension = Path.GetExtension(fileName);
        var allowed = kind switch
        {
            IngestSubmissionKind.MarkdownFile => _markdownExtensions,
            IngestSubmissionKind.PdfFile => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf" },
            IngestSubmissionKind.OfficeFile => _officeExtensions,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a file-based submission kind."),
        };

        if (!allowed.Contains(extension))
        {
            return new IngestSubmissionValidationResult(false,
                $"Unsupported file extension '{extension}' for {DescribeKind(kind)}. Supported formats: {string.Join(", ", allowed)}.",
                IngestSubmissionValidationErrorKind.UnsupportedMediaType);
        }

        if (sizeBytes <= 0)
        {
            return new IngestSubmissionValidationResult(false, "The submitted file is empty.", IngestSubmissionValidationErrorKind.UnprocessableEntity);
        }

        return IngestSubmissionValidationResult.Valid;
    }

    /// <summary>Maximum accepted user-prompt length after trim (FR-010, contracts: userPromptMaxLength).</summary>
    public const int UserPromptMaxLength = 8000;

    /// <summary>
    /// Validates the optional per-submission steering prompt (FR-007, FR-010).
    /// Empty/whitespace means "use the default" and normalizes to null.
    /// </summary>
    public IngestSubmissionValidationResult ValidateUserPrompt(string? userPrompt, out string? normalizedPrompt)
    {
        normalizedPrompt = string.IsNullOrWhiteSpace(userPrompt) ? null : userPrompt.Trim();
        if (normalizedPrompt is not null && normalizedPrompt.Length > UserPromptMaxLength)
        {
            normalizedPrompt = null;
            return new IngestSubmissionValidationResult(false,
                $"user_prompt_too_long: The user prompt exceeds the maximum of {UserPromptMaxLength} characters.",
                IngestSubmissionValidationErrorKind.BadRequest);
        }

        return IngestSubmissionValidationResult.Valid;
    }

    /// <summary>
    /// Validates the optional per-submission convert-step configuration against the
    /// registry (FR-011, FR-013): unknown step → 400, step not applicable to the
    /// submitted kind → 400, required step disabled → 422. All before task creation.
    /// </summary>
    public IngestSubmissionValidationResult ValidateConvertSteps(
        string kindLabel, IReadOnlyDictionary<string, bool>? requestedSteps)
    {
        if (requestedSteps is null || requestedSteps.Count == 0)
        {
            return IngestSubmissionValidationResult.Valid;
        }

        foreach (var (name, enabled) in requestedSteps)
        {
            var step = ConvertStepRegistry.TryGet(name);
            if (step is null)
            {
                return new IngestSubmissionValidationResult(false,
                    $"unknown_convert_step: '{name}' is not a registered convert step.",
                    IngestSubmissionValidationErrorKind.BadRequest);
            }

            if (!step.AppliesTo.Contains(kindLabel))
            {
                return new IngestSubmissionValidationResult(false,
                    $"convert_step_not_applicable: step '{name}' does not apply to {kindLabel} submissions.",
                    IngestSubmissionValidationErrorKind.BadRequest);
            }

            if (!enabled && step.RequiredFor.Contains(kindLabel))
            {
                return new IngestSubmissionValidationResult(false,
                    $"convert_step_required: step '{name}' cannot be disabled for {kindLabel} submissions — " +
                    "binary formats must be converted to Markdown to be usable by the agent.",
                    IngestSubmissionValidationErrorKind.UnprocessableEntity);
            }
        }

        return IngestSubmissionValidationResult.Valid;
    }

    public static bool TryParseKind(string? rawKind, out IngestSubmissionKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(rawKind))
        {
            return false;
        }

        switch (rawKind.Trim().ToLowerInvariant())
        {
            case "url": kind = IngestSubmissionKind.Url; return true;
            case "markdown_file": kind = IngestSubmissionKind.MarkdownFile; return true;
            case "pdf_file": kind = IngestSubmissionKind.PdfFile; return true;
            case "office_file": kind = IngestSubmissionKind.OfficeFile; return true;
            default: return false;
        }
    }

    private static string DescribeKind(IngestSubmissionKind kind) => kind switch
    {
        IngestSubmissionKind.MarkdownFile => "a Markdown file",
        IngestSubmissionKind.PdfFile => "a PDF file",
        IngestSubmissionKind.OfficeFile => "an Office document",
        _ => "this submission kind",
    };
}
