namespace Grimoire.Hub.ApiErrors;

/// <summary>
/// One authored failure the HTTP API can return (ADR-026; 024 FR-001/FR-002/FR-016).
///
/// <para>
/// The split between the four fields is the whole point of the type. <see cref="Code"/> is for
/// machines — logs, metrics, tests, operational tooling — and is a stability contract: renaming one
/// breaks consumers that key on it. <see cref="Title"/> and <see cref="Detail"/> are for the person
/// who hit the failure, and must never contain the code, a status number, or a serialized
/// structure. Before this feature the Hub shipped some responses carrying only the code, which is
/// how <c>conversation_already_active</c> ended up displayed to users as their error message.
/// </para>
/// </summary>
/// <param name="Code">
/// Stable, <c>snake_case</c>, unique across the catalogue. Never displayed to the user.
/// </param>
/// <param name="Status">The HTTP status this failure is answered with (400–599).</param>
/// <param name="Title">Short human-readable headline for the failure class.</param>
/// <param name="Detail">
/// The actionable sentence: what happened and, where the user can resolve it, what to do. A call
/// site may override this with something more specific (naming a task id, a limit, a field); it may
/// not override <see cref="Code"/>, <see cref="Status"/>, or <see cref="Title"/>.
/// </param>
public sealed record ApiErrorDefinition(string Code, int Status, string Title, string Detail)
{
    /// <summary>
    /// Validation lives in property initializers rather than a constructor body because a
    /// positional record has no constructor body to put it in — and it belongs at construction
    /// rather than in a separate validator, so a malformed entry cannot exist even briefly. The
    /// primary-constructor parameters are in scope here, which is what lets the cross-field
    /// "detail must not quote its own code" check sit on <see cref="Detail"/>.
    /// </summary>
    public string Code { get; } = string.IsNullOrWhiteSpace(Code)
        ? throw new ArgumentException("An API error definition needs a code.", nameof(Code))
        : Code;

    public int Status { get; } = Status is < 400 or > 599
        ? throw new ArgumentOutOfRangeException(
            nameof(Status), Status, $"API error '{Code}' must carry a 4xx or 5xx status.")
        : Status;

    public string Title { get; } = ValidateUserFacingText(Code, Title, nameof(Title));

    public string Detail { get; } = ValidateUserFacingText(Code, Detail, nameof(Detail));

    /// <summary>
    /// Non-empty, and free of the entry's own identifier. The identifier is for logs and tooling;
    /// a message that quotes it defeats the separation this type exists to enforce (024 FR-002) —
    /// and quoting it is exactly how <c>conversation_already_active</c> used to reach users as
    /// their error message.
    /// </summary>
    private static string ValidateUserFacingText(string code, string text, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                $"API error '{code}' needs a non-empty {parameterName.ToLowerInvariant()}.", parameterName);
        }

        if (text.Contains(code, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"API error '{code}' leaks its own identifier into its {parameterName.ToLowerInvariant()}. " +
                "Codes are for machines; title and detail are for people.", parameterName);
        }

        return text;
    }
}
