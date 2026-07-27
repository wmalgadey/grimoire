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
public sealed class QuerySubmissionValidator
{
    public const int PromptMaxLength = 8000;

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
