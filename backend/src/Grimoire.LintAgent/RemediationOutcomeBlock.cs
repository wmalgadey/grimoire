using System.Text.Json;

namespace Grimoire.LintAgent;

/// <summary>One remediation-execution mode's re-verification judgment, mechanically extracted off the agent's final narrative.</summary>
public sealed record RemediationOutcomeEntry(string Outcome, string? Reason);

/// <summary>
/// Mechanical extraction of the agent's <c>```remediation-outcome</c> fenced block from
/// its final narrative (T035, 015-lint-board-parity, ADR-018 FR-018). Mirrors
/// <see cref="ProposedActionsBlock"/> exactly — a fixed transport protocol the
/// remediation-execution instructions (data/agents/lint/system-prompt.md, T036) tell the
/// agent to end its message with. This code only moves the agent's structured verdict
/// onto the terminal event (Constitution Principle V): whether the proposal is still
/// applicable, and every word of the reason, is agent judgment exercised in the
/// instructions; nothing here computes or second-guesses it. Entry-level tolerance
/// mirrors the Hub's terminal-event parser: a block that is not valid JSON, or an
/// "outcome" value other than <c>applied</c>/<c>not_applicable</c>, yields no parsed
/// outcome (the narrative is left intact so the raw block stays visible) — the run's
/// actual mechanical facts (did anything get written, was anything denied) remain the
/// harness's fallback authority in that case (see
/// <c>RemediationExecutionIntentHandler.ExecuteAsync</c>).
/// </summary>
public static class RemediationOutcomeBlock
{
    private const string FenceOpen = "```remediation-outcome";
    private const string FenceClose = "```";

    public const string OutcomeApplied = "applied";
    public const string OutcomeNotApplicable = "not_applicable";

    /// <summary>
    /// Splits the narrative into the report text (block removed) and the parsed outcome
    /// entry, if any. A narrative without the block, or with an unparseable/invalid one,
    /// round-trips unchanged with a null entry.
    /// </summary>
    public static (string Narrative, RemediationOutcomeEntry? Outcome) Extract(string narrative)
    {
        var openIndex = FindFenceLineStart(narrative, FenceOpen);
        if (openIndex < 0)
        {
            return (narrative, null);
        }

        var payloadStart = narrative.IndexOf('\n', openIndex);
        if (payloadStart < 0)
        {
            return (narrative, null);
        }
        payloadStart += 1;

        var closeIndex = FindFenceLineStart(narrative, FenceClose, payloadStart);
        if (closeIndex < 0)
        {
            return (narrative, null);
        }

        var payload = narrative[payloadStart..closeIndex];
        var outcome = TryParseOutcome(payload);

        var blockEnd = narrative.IndexOf('\n', closeIndex);
        blockEnd = blockEnd < 0 ? narrative.Length : blockEnd + 1;
        var stripped = (narrative[..openIndex] + narrative[blockEnd..]).TrimEnd() + "\n";

        // Unparseable/invalid payload: keep the raw block visible rather than silently
        // dropping it (mirrors ProposedActionsBlock's "leave narrative intact" choice for
        // the truly malformed case) — but still strip a *recognized-but-invalid* block
        // (e.g. bad outcome value) since it parsed as JSON, just not to a usable verdict.
        return outcome is null && !IsValidJsonObject(payload)
            ? (narrative, null)
            : (stripped, outcome);
    }

    private static int FindFenceLineStart(string text, string fence, int from = 0)
    {
        var searchFrom = from;
        var found = -1;
        while (searchFrom <= text.Length - fence.Length)
        {
            var idx = text.IndexOf(fence, searchFrom, StringComparison.Ordinal);
            if (idx < 0)
            {
                break;
            }

            var atLineStart = idx == 0 || text[idx - 1] == '\n';
            if (atLineStart)
            {
                found = idx;
                if (fence == FenceClose)
                {
                    return found;
                }
            }

            searchFrom = idx + fence.Length;
        }

        return found;
    }

    private static bool IsValidJsonObject(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static RemediationOutcomeEntry? TryParseOutcome(string payload)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var outcome = TryGetNonEmptyString(document.RootElement, "outcome");
            if (outcome is not (OutcomeApplied or OutcomeNotApplicable))
            {
                return null;
            }

            var reason = TryGetNonEmptyString(document.RootElement, "reason");
            return new RemediationOutcomeEntry(outcome, reason);
        }
    }

    private static string? TryGetNonEmptyString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String &&
           property.GetString() is { } value &&
           !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
