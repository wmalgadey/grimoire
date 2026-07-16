using System.Security.Cryptography;
using System.Text;

namespace Grimoire.IngestAgent.AgentCore;

/// <summary>
/// The single System Prompt Document loaded for a run (ADR-007). Its content is the
/// agent's entire system prompt, verbatim; the SHA-256 is recorded in the task artifact.
/// </summary>
public sealed record LoadedSystemPrompt(string Path, string Content, string Sha256);

/// <summary>Failure record returned by <see cref="SystemPromptLoader"/>.</summary>
public sealed record SystemPromptLoadFailure(string Reason);

/// <summary>
/// Loads exactly one instruction document verbatim (ADR-007: single system-prompt file,
/// no directory scan, no concatenation). Fail-closed: missing, unreadable, or
/// effectively empty document ⇒ load failure before any wiki write (FR-003, SC-002).
/// Also used for the default-user-prompt document, which shares the same fail-closed
/// semantics.
/// </summary>
public sealed class SystemPromptLoader
{
    public async Task<OneOf<LoadedSystemPrompt, SystemPromptLoadFailure>> LoadAsync(
        string documentPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(documentPath))
        {
            return new SystemPromptLoadFailure(
                $"Instruction document not found at '{documentPath}'. Cannot start a run without agent operating rules.");
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(documentPath, Encoding.UTF8, cancellationToken);
        }
        catch (Exception ex)
        {
            return new SystemPromptLoadFailure(
                $"Cannot read instruction document at '{documentPath}': {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return new SystemPromptLoadFailure(
                $"Instruction document at '{documentPath}' is empty or whitespace-only. Cannot start a run without agent operating rules.");
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new LoadedSystemPrompt(documentPath, content, sha256);
    }
}
