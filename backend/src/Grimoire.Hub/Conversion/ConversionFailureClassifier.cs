namespace Grimoire.Hub.Conversion;

/// <summary>
/// Maps raw fetch/conversion faults to a plain-language reason (FR-009, spec.md Edge Cases).
/// External tools (MarkItDown) can emit multi-line stack traces on failure; this keeps only the
/// last non-empty line — typically the actual exception message — and bounds its length, so the
/// board never shows a raw traceback as a "human-readable" failure reason.
/// </summary>
public static class ConversionFailureClassifier
{
    private const int MaxReasonLength = 300;

    public static string Classify(string rawReason)
    {
        if (string.IsNullOrWhiteSpace(rawReason))
        {
            return "Conversion failed for an unknown reason.";
        }

        var lastMeaningfulLine = rawReason
            .Split('\n')
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Length > 0) ?? rawReason.Trim();

        return lastMeaningfulLine.Length > MaxReasonLength
            ? string.Concat(lastMeaningfulLine.AsSpan(0, MaxReasonLength), "…")
            : lastMeaningfulLine;
    }
}
