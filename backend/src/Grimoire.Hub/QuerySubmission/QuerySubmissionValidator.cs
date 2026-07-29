namespace Grimoire.Hub.QuerySubmission;

/// <summary>Result of validating a Query Turn submission (FR-004).</summary>
public sealed record QuerySubmissionValidationResult(bool IsValid, string? ErrorMessage)
{
    public static readonly QuerySubmissionValidationResult Valid = new(true, null);
}

/// <summary>
/// Server-side re-validation of the Query Prompt (FR-004) — mirrors the client-side
/// check in <c>QueryPromptForm.svelte</c>; this is the defensive backstop, not the
/// user-facing UX (contracts/query-conversation-api.md).
/// </summary>
public sealed partial class QuerySubmissionValidator
{
    public const int PromptMaxLength = 8000;

    // 011-query-conversations (contracts/query-conversation-api.md): the conversationId
    // names the Conversation Record file, so path safety is enforced server-side.
    // Source-generated (no runtime regex compilation on the submission hot path).
    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ConversationIdPattern();

    public QuerySubmissionValidationResult ValidateConversationId(string? conversationId)
    {
        if (conversationId is null || !ConversationIdPattern().IsMatch(conversationId))
        {
            return new QuerySubmissionValidationResult(false,
                "conversationId must match ^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$.");
        }

        return QuerySubmissionValidationResult.Valid;
    }

    public QuerySubmissionValidationResult ValidatePrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new QuerySubmissionValidationResult(false, "prompt must not be empty or whitespace-only.");
        }

        if (prompt.Trim().Length > PromptMaxLength)
        {
            return new QuerySubmissionValidationResult(false,
                $"prompt exceeds the maximum of {PromptMaxLength} characters.");
        }

        return QuerySubmissionValidationResult.Valid;
    }
}
