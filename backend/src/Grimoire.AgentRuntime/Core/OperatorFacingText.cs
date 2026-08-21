namespace Grimoire.AgentRuntime.Core;

/// <summary>
/// Formatting rule for text that ends up as a run's <c>failure_reason</c>.
/// <para>
/// Both task-artifact writers persist only <c>failure_reason.Split('\n')[0]</c>, so a
/// multi-line message silently loses everything after its first line, and the frontmatter
/// has no business carrying a pathologically long provider body. Every harness-owned
/// failure message that originates outside our own source — a provider error body
/// (<see cref="ModelApiException"/>), a refusal explanation
/// (<see cref="ModelRefusalException"/>) — is passed through
/// <see cref="SingleLineCapped"/> once, at the point the external text is composed in.
/// </para>
/// </summary>
public static class OperatorFacingText
{
    /// <summary>Maximum length of a composed failure reason, before the ellipsis.</summary>
    public const int MaxLength = 500;

    /// <summary>Collapses <paramref name="text"/> to a single trimmed line and caps its length.</summary>
    public static string SingleLineCapped(string text)
    {
        var singleLine = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return singleLine.Length <= MaxLength
            ? singleLine
            : singleLine[..MaxLength] + "…";
    }
}
