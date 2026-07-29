using System.Text.RegularExpressions;

namespace Grimoire.AgentRuntime.Composition;

/// <summary>
/// The single implementation of credential-bearing error-text sanitization (ADR-013;
/// consolidates the formerly duplicated SanitizeErrorText in both hosts' Program.cs —
/// identical output for identical input, since terminal `failed` event text is
/// observable behavior, FR-008). The empty-message fallback is per-agent frozen text
/// ("Unknown ingest error." / "Unknown query error.") supplied by the host.
/// </summary>
public static class ErrorSanitizer
{
    public static string Sanitize(string message, string emptyMessageFallback)
    {
        if (string.IsNullOrWhiteSpace(message))
            return emptyMessageFallback;

        var sanitized = message;
        var envAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(envAuthToken))
            sanitized = sanitized.Replace(envAuthToken, "[REDACTED]", StringComparison.Ordinal);

        sanitized = Regex.Replace(sanitized, "sk-ant-[A-Za-z0-9_-]+", "[REDACTED]",
            RegexOptions.CultureInvariant);
        return sanitized;
    }
}
