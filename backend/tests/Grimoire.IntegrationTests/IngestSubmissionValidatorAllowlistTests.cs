using Grimoire.Domain.Ingest;
using Grimoire.Hub.IngestSubmission;

namespace Grimoire.IntegrationTests;

/// <summary>
/// ADR-034 R4 (027-host-stability, research.md D6): a filename-derived value that selects a
/// converter or code path must be validated against a fixed allowlist before any conversion
/// or storage code runs. <see cref="IngestSubmissionValidator"/>'s existing fixed
/// <c>HashSet&lt;string&gt;</c> allowlists already implement this — this pins it with a
/// dedicated, intent-named case against the real validation entry point rather than relying
/// on incidental coverage from <c>IngestSubmissionApiTests</c>/<c>IngestConvertStepTests</c>.
/// No production code change: this is a Feature-Scoped Invariant (Constitution Principle
/// III), covered by a classicist behavioral test, never a reflection/IL structural test.
/// </summary>
public class IngestSubmissionValidatorAllowlistTests
{
    [Theory]
    [InlineData(IngestSubmissionKind.OfficeFile, "invoice.exe")]
    [InlineData(IngestSubmissionKind.MarkdownFile, "notes.sh")]
    [InlineData(IngestSubmissionKind.PdfFile, "report.pdf.exe")]
    public void ValidateFile_RejectsUnlistedExtension_BeforeAnyConversionOrStorage(
        IngestSubmissionKind kind, string fileName)
    {
        var validator = new IngestSubmissionValidator();

        var result = validator.ValidateFile(kind, fileName, sizeBytes: 1024);

        Assert.False(result.IsValid);
        Assert.Equal(IngestSubmissionValidationErrorKind.UnsupportedMediaType, result.ErrorKind);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData(IngestSubmissionKind.MarkdownFile, "page.md")]
    [InlineData(IngestSubmissionKind.PdfFile, "report.pdf")]
    [InlineData(IngestSubmissionKind.OfficeFile, "sheet.xlsx")]
    public void ValidateFile_AllowsListedExtension(IngestSubmissionKind kind, string fileName)
    {
        var validator = new IngestSubmissionValidator();

        var result = validator.ValidateFile(kind, fileName, sizeBytes: 1024);

        Assert.True(result.IsValid);
    }
}
