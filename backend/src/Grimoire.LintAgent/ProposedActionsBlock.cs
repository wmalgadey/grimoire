using System.Text.Json;
using Grimoire.AgentRuntime.RunEvents;

namespace Grimoire.LintAgent;

/// <summary>
/// Mechanical extraction of the agent's <c>```proposed-actions</c> fenced block from its
/// final narrative (015-lint-board-parity T025, ADR-018). The block is a fixed transport
/// protocol the system prompt instructs the agent to use — like the NDJSON event channel
/// itself, this code only moves the agent's structured output onto the terminal event
/// (Constitution Principle V): which findings become proposals, and every word of
/// title/description/targetPath, is agent judgment exercised in
/// <c>agents/lint/system-prompt.md</c>; nothing here filters, merges, or rewrites
/// content. Entry-level tolerance mirrors the Hub's
/// <c>TolerantProposedActionListConverter</c>: an entry missing a non-empty
/// <c>title</c>/<c>description</c> is skipped, and a block that is not valid JSON at all
/// yields no proposals with the narrative left untouched (the raw block then stays
/// visible in the Findings Report instead of being silently swallowed).
/// </summary>
public static class ProposedActionsBlock
{
    private const string FenceOpen = "```proposed-actions";
    private const string FenceClose = "```";

    /// <summary>
    /// Splits the narrative into the report text (block removed) and the proposal
    /// entries. A narrative without the block round-trips unchanged with zero entries —
    /// a clean run proposes nothing (spec US3 scenario 2).
    /// </summary>
    public static (string Narrative, IReadOnlyList<ProposedActionRecord> Actions) Extract(string narrative)
    {
        var openIndex = FindFenceLineStart(narrative, FenceOpen);
        if (openIndex < 0)
        {
            return (narrative, []);
        }

        var payloadStart = narrative.IndexOf('\n', openIndex);
        if (payloadStart < 0)
        {
            return (narrative, []);
        }
        payloadStart += 1;

        var closeIndex = FindFenceLineStart(narrative, FenceClose, payloadStart);
        if (closeIndex < 0)
        {
            return (narrative, []);
        }

        var payload = narrative[payloadStart..closeIndex];
        var actions = TryParseActions(payload);
        if (actions is null)
        {
            // Unparseable JSON: carry no proposals and keep the narrative intact so the
            // malformed block is at least visible in the Findings Report.
            return (narrative, []);
        }

        var blockEnd = narrative.IndexOf('\n', closeIndex);
        blockEnd = blockEnd < 0 ? narrative.Length : blockEnd + 1;
        var stripped = (narrative[..openIndex] + narrative[blockEnd..]).TrimEnd() + "\n";

        return (stripped, actions);
    }

    /// <summary>Finds the offset of the last line that starts with <paramref name="fence"/> (the block is instructed to be the final element of the message).</summary>
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
                    // The close fence is the first "```" line after the payload start.
                    return found;
                }
            }

            searchFrom = idx + fence.Length;
        }

        return found;
    }

    private static IReadOnlyList<ProposedActionRecord>? TryParseActions(string payload)
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
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var actions = new List<ProposedActionRecord>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var title = TryGetNonEmptyString(element, "title");
                var description = TryGetNonEmptyString(element, "description");
                if (title is null || description is null)
                {
                    continue;
                }

                actions.Add(new ProposedActionRecord(title, description, TryGetNonEmptyString(element, "targetPath")));
            }

            return actions;
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
