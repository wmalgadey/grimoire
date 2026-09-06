using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Grimoire.Hub.RemediationTasks;

/// <summary>One parsed entry of a Remediation Task Record (015-lint-board-parity data-model.md "Appended entries").</summary>
public abstract record RemediationTaskRecordEntry
{
    /// <summary>Exactly one, written at creation: the verbatim agent-authored proposal (Principle V).</summary>
    public sealed record Proposal(string Title, string Description, string? TargetPath) : RemediationTaskRecordEntry;

    /// <summary>Human-attached information/instructions (0..N, FR-011), verbatim.</summary>
    public sealed record Context(DateTimeOffset AttachedAt, string Text) : RemediationTaskRecordEntry;

    /// <summary>One side of a human⇄agent exchange (0..N, FR-012).</summary>
    public sealed record Message(string Sender, DateTimeOffset Timestamp, string Text) : RemediationTaskRecordEntry;

    /// <summary>Exactly one, written at the terminal transition; <see cref="Reason"/> nullable except for <c>failed</c>/<c>not_applicable</c>.</summary>
    public sealed record Outcome(string State, string? Reason, DateTimeOffset CompletedAt, string Summary) : RemediationTaskRecordEntry;
}

/// <summary>Result of parsing a Remediation Task Record file (mirrors <c>QueryConversationRecordParseResult</c>, ADR-014 parsing rules).</summary>
public abstract record RemediationTaskRecordParseResult
{
    /// <summary>
    /// All complete entries, in file order. <see cref="DroppedTrailingFragment"/> is true
    /// when a trailing incomplete block (crash mid-append) was dropped — the file is
    /// still readable up to the recorded prefix.
    /// </summary>
    public sealed record Parsed(IReadOnlyList<RemediationTaskRecordEntry> Entries, bool DroppedTrailingFragment)
        : RemediationTaskRecordParseResult;

    /// <summary>Structural violation (bad frontmatter, malformed bookkeeping, body shorter than declared length) — fail closed.</summary>
    public sealed record Unreadable(string Reason) : RemediationTaskRecordParseResult;
}

/// <summary>
/// Writer and parser for the <c>grimoire-remediation-task/1</c> Remediation Task Record
/// format (015-lint-board-parity data-model.md, ADR-014's Conversation Record shape one
/// level down). Bodies are <b>length-delimited, never sentinel-delimited</b>: the parser
/// slices every body by its declared UTF-16 code-unit length (<c>*_chars</c>) and never
/// scans body content for headings or comment markers, so untrusted agent- or
/// human-authored text containing <c>&lt;!-- grimoire:proposal --&gt;</c> or <c>##</c>
/// headings cannot break or forge structure (same mechanism as
/// <c>QueryConversationRecordFormat</c>). String values inside bookkeeping comments are
/// JSON-escaped with <c>--&gt;</c> neutralized.
/// </summary>
public static class RemediationTaskRecordFormat
{
    public const string RecordFormatVersion = "grimoire-remediation-task/1";
    public const string SenderHuman = "human";
    public const string SenderAgent = "agent";

    private const string ProposalSentinel = "<!-- grimoire:proposal";
    private const string ContextSentinel = "<!-- grimoire:context";
    private const string MessageSentinel = "<!-- grimoire:message";
    private const string OutcomeSentinel = "<!-- grimoire:outcome";
    private const string CommentClose = "-->";

    private static readonly string[] _sentinels =
    [
        ProposalSentinel,
        ContextSentinel,
        MessageSentinel,
        OutcomeSentinel,
    ];

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Encoding used for record files (UTF-8, no BOM — the parser slices by char offsets).</summary>
    public static Encoding Encoding => _utf8NoBom;

    // ------------------------------------------------------------------ writer

    /// <summary>Frontmatter + document heading, written once at task materialization (identity bookkeeping only — the SQLite row is the live state authority).</summary>
    public static string BuildRecordHeader(string taskId, string runId, DateTimeOffset proposedAt)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("task_id: ").Append(taskId).Append('\n');
        sb.Append("run_id: ").Append(runId).Append('\n');
        sb.Append("proposed_at: ").Append(proposedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("record_format: ").Append(RecordFormatVersion).Append('\n');
        sb.Append("---\n");
        sb.Append('\n');
        sb.Append("# Remediation Task ").Append(taskId).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>The proposal block (exactly one per record, immediately after the header).</summary>
    public static string BuildProposalBlock(RemediationTaskRecordEntry.Proposal proposal)
    {
        var sb = new StringBuilder();
        sb.Append(ProposalSentinel).Append('\n');
        sb.Append("title_chars: ").Append(proposal.Title.Length).Append('\n');
        sb.Append("description_chars: ").Append(proposal.Description.Length).Append('\n');
        sb.Append("target_path: ").Append(NullableString(proposal.TargetPath)).Append('\n');
        sb.Append(CommentClose).Append('\n');
        sb.Append('\n');
        sb.Append("## Proposal\n");
        sb.Append('\n');
        sb.Append(proposal.Title).Append('\n');
        sb.Append('\n');
        sb.Append(proposal.Description).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    public static string BuildContextBlock(RemediationTaskRecordEntry.Context context)
    {
        var sb = new StringBuilder();
        sb.Append(ContextSentinel).Append('\n');
        sb.Append("attached_at: ").Append(context.AttachedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("text_chars: ").Append(context.Text.Length).Append('\n');
        sb.Append(CommentClose).Append('\n');
        sb.Append('\n');
        sb.Append("## Attached context\n");
        sb.Append('\n');
        sb.Append(context.Text).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    public static string BuildMessageBlock(RemediationTaskRecordEntry.Message message)
    {
        if (message.Sender is not (SenderHuman or SenderAgent))
        {
            throw new ArgumentException($"Unknown message sender '{message.Sender}' (expected '{SenderHuman}' or '{SenderAgent}').");
        }

        var sb = new StringBuilder();
        sb.Append(MessageSentinel).Append('\n');
        sb.Append("sender: ").Append(message.Sender).Append('\n');
        sb.Append("timestamp: ").Append(message.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("text_chars: ").Append(message.Text.Length).Append('\n');
        sb.Append(CommentClose).Append('\n');
        sb.Append('\n');
        sb.Append("## Message — ").Append(message.Sender).Append('\n');
        sb.Append('\n');
        sb.Append(message.Text).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>The outcome block (exactly one, at the terminal transition). <c>summary_chars</c> length-delimits the optional agent summary (empty allowed).</summary>
    public static string BuildOutcomeBlock(RemediationTaskRecordEntry.Outcome outcome)
    {
        var sb = new StringBuilder();
        sb.Append(OutcomeSentinel).Append('\n');
        sb.Append("state: ").Append(outcome.State).Append('\n');
        sb.Append("reason: ").Append(NullableString(outcome.Reason)).Append('\n');
        sb.Append("completed_at: ").Append(outcome.CompletedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("summary_chars: ").Append(outcome.Summary.Length).Append('\n');
        sb.Append(CommentClose).Append('\n');
        sb.Append('\n');
        sb.Append("## Outcome — ").Append(outcome.State).Append('\n');
        sb.Append('\n');
        sb.Append(outcome.Summary).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    private static string NullableString(string? value) => value is null ? "null" : EscapeString(value);

    /// <summary>
    /// Double-quoted JSON-escaped string; the default encoder escapes <c>&gt;</c>, so
    /// <c>--&gt;</c> can never appear literally inside a bookkeeping comment (same guard
    /// as <c>QueryConversationRecordFormat.EscapeString</c>).
    /// </summary>
    private static string EscapeString(string value)
    {
        var json = JsonSerializer.Serialize(value);
        if (json.Contains(CommentClose, StringComparison.Ordinal))
        {
            json = json.Replace(CommentClose, "--\\u003e", StringComparison.Ordinal);
        }

        return json;
    }

    // ------------------------------------------------------------------ parser

    /// <summary>
    /// Parses a record back into its complete entries (ADR-014 parsing rules 1–5 applied
    /// to this format): frontmatter handshake, sentinel scanning strictly outside
    /// length-consumed body ranges, unknown bookkeeping keys tolerated, a trailing
    /// incomplete block dropped (readable, flagged), any other structural violation
    /// classifying the record as unreadable.
    /// </summary>
    public static RemediationTaskRecordParseResult Parse(string content)
    {
        var pos = 0;

        if (TryParseFrontmatter(content, ref pos) is { } frontmatterError)
        {
            return new RemediationTaskRecordParseResult.Unreadable(frontmatterError);
        }

        var entries = new List<RemediationTaskRecordEntry>();
        var droppedTrailingFragment = false;

        while (true)
        {
            var (sentinelPos, sentinel) = FindSentinelLineStart(content, pos);
            if (sentinel is null)
            {
                break;
            }

            pos = sentinelPos;
            TryReadLine(content, ref pos, out _); // consume the sentinel line itself

            if (!TryReadBookkeepingBlock(content, ref pos, out var bookkeepingLines))
            {
                // Trailing incomplete block (crash mid-append): drop the fragment.
                droppedTrailingFragment = true;
                break;
            }

            if (!TryParseBookkeeping(bookkeepingLines, out var fields, out var bookkeepingError))
            {
                return new RemediationTaskRecordParseResult.Unreadable($"malformed entry bookkeeping: {bookkeepingError}");
            }

            if (ParseEntryBody(sentinel, content, ref pos, fields, entries) is { } structuralError)
            {
                return new RemediationTaskRecordParseResult.Unreadable(structuralError);
            }
        }

        return new RemediationTaskRecordParseResult.Parsed(entries, droppedTrailingFragment);
    }

    /// <summary>
    /// Consumes the frontmatter handshake — opening <c>---</c>, flat key/value lines, closing
    /// <c>---</c>, supported <c>record_format</c> — leaving <paramref name="pos"/> at the first
    /// body character. Returns the unreadability reason, or <c>null</c> when the handshake held.
    /// </summary>
    private static string? TryParseFrontmatter(string content, ref int pos)
    {
        if (!TryReadLine(content, ref pos, out var line) || line != "---")
        {
            return "missing frontmatter opening '---'";
        }

        var frontmatter = new Dictionary<string, string>(StringComparer.Ordinal);
        while (true)
        {
            if (!TryReadLine(content, ref pos, out line))
            {
                return "truncated frontmatter (no closing '---')";
            }

            if (line == "---")
            {
                break;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return $"malformed frontmatter line: '{line}'";
            }

            frontmatter[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        if (!frontmatter.TryGetValue("record_format", out var format) || format != RecordFormatVersion)
        {
            return $"unsupported record_format '{frontmatter.GetValueOrDefault("record_format", "(missing)")}'";
        }

        return null;
    }

    /// <summary>
    /// Reads an entry's bookkeeping comment lines up to <see cref="CommentClose"/>. Returns false
    /// when the input ends first — the crash-mid-append fragment the caller drops.
    /// </summary>
    private static bool TryReadBookkeepingBlock(string content, ref int pos, out List<string> bookkeepingLines)
    {
        bookkeepingLines = [];
        while (TryReadLine(content, ref pos, out var line))
        {
            if (line == CommentClose)
            {
                return true;
            }

            bookkeepingLines.Add(line);
        }

        return false;
    }

    /// <summary>Routes an entry to the body parser its sentinel names.</summary>
    private static string? ParseEntryBody(
        string sentinel, string content, ref int pos, Dictionary<string, string> fields, List<RemediationTaskRecordEntry> entries)
        => sentinel switch
        {
            ProposalSentinel => TryParseProposalBody(content, ref pos, fields, entries),
            ContextSentinel => TryParseContextBody(content, ref pos, fields, entries),
            MessageSentinel => TryParseMessageBody(content, ref pos, fields, entries),
            OutcomeSentinel => TryParseOutcomeBody(content, ref pos, fields, entries),
            _ => "unknown sentinel",
        };

    // ------------------------------------------------------------ entry parsers

    private static string? TryParseProposalBody(
        string content, ref int pos, Dictionary<string, string> fields, List<RemediationTaskRecordEntry> entries)
    {
        if (!TryGetInt(fields, "title_chars", out var titleChars) ||
            !TryGetInt(fields, "description_chars", out var descriptionChars))
        {
            return "proposal entry is missing a valid title_chars/description_chars field";
        }

        if (!TryGetNullableString(fields, "target_path", out var targetPath))
        {
            return "proposal entry has a malformed target_path field";
        }

        if (!TryExpectLine(content, ref pos, string.Empty) ||
            !TryExpectLine(content, ref pos, "## Proposal") ||
            !TryExpectLine(content, ref pos, string.Empty))
        {
            return "malformed proposal section structure";
        }

        if (!TrySliceBody(content, ref pos, titleChars, out var title) ||
            !TryExpectLine(content, ref pos, string.Empty))
        {
            return "proposal title body shorter than declared title_chars length";
        }

        if (!TrySliceBody(content, ref pos, descriptionChars, out var description) ||
            !TryExpectLine(content, ref pos, string.Empty))
        {
            return "proposal description body shorter than declared description_chars length";
        }

        entries.Add(new RemediationTaskRecordEntry.Proposal(title, description, targetPath));
        return null;
    }

    private static string? TryParseContextBody(
        string content, ref int pos, Dictionary<string, string> fields, List<RemediationTaskRecordEntry> entries)
    {
        if (!TryGetTimestamp(fields, "attached_at", out var attachedAt))
        {
            return "context entry is missing a valid attached_at field";
        }

        if (!TryGetInt(fields, "text_chars", out var textChars))
        {
            return "context entry is missing a valid text_chars field";
        }

        if (!TryExpectLine(content, ref pos, string.Empty) ||
            !TryExpectLine(content, ref pos, "## Attached context") ||
            !TryExpectLine(content, ref pos, string.Empty))
        {
            return "malformed attached-context section structure";
        }

        if (!TrySliceBody(content, ref pos, textChars, out var text) ||
            !TryExpectLine(content, ref pos, string.Empty))
        {
            return "context body shorter than declared text_chars length";
        }

        entries.Add(new RemediationTaskRecordEntry.Context(attachedAt, text));
        return null;
    }

    private static string? TryParseMessageBody(
        string content, ref int pos, Dictionary<string, string> fields, List<RemediationTaskRecordEntry> entries)
    {
        if (!fields.TryGetValue("sender", out var sender) || sender is not (SenderHuman or SenderAgent))
        {
            return "message entry is missing a valid sender field";
        }

        if (!TryGetTimestamp(fields, "timestamp", out var timestamp))
        {
            return "message entry is missing a valid timestamp field";
        }

        if (!TryGetInt(fields, "text_chars", out var textChars))
        {
            return "message entry is missing a valid text_chars field";
        }

        if (!TryExpectLine(content, ref pos, string.Empty) ||
            !TryExpectLine(content, ref pos, $"## Message — {sender}") ||
            !TryExpectLine(content, ref pos, string.Empty))
        {
            return "malformed message section structure";
        }

        if (!TrySliceBody(content, ref pos, textChars, out var text) ||
            !TryExpectLine(content, ref pos, string.Empty))
        {
            return "message body shorter than declared text_chars length";
        }

        entries.Add(new RemediationTaskRecordEntry.Message(sender, timestamp, text));
        return null;
    }

    private static string? TryParseOutcomeBody(
        string content, ref int pos, Dictionary<string, string> fields, List<RemediationTaskRecordEntry> entries)
    {
        if (!fields.TryGetValue("state", out var state) || state.Length == 0)
        {
            return "outcome entry is missing a state field";
        }

        if (!TryGetNullableString(fields, "reason", out var reason))
        {
            return "outcome entry has a malformed reason field";
        }

        if (!TryGetTimestamp(fields, "completed_at", out var completedAt))
        {
            return "outcome entry is missing a valid completed_at field";
        }

        if (!TryGetInt(fields, "summary_chars", out var summaryChars))
        {
            return "outcome entry is missing a valid summary_chars field";
        }

        if (!TryExpectLine(content, ref pos, string.Empty) ||
            !TryExpectLine(content, ref pos, $"## Outcome — {state}") ||
            !TryExpectLine(content, ref pos, string.Empty))
        {
            return "malformed outcome section structure";
        }

        if (!TrySliceBody(content, ref pos, summaryChars, out var summary) ||
            !TryExpectLine(content, ref pos, string.Empty))
        {
            return "outcome summary body shorter than declared summary_chars length";
        }

        entries.Add(new RemediationTaskRecordEntry.Outcome(state, reason, completedAt, summary));
        return null;
    }

    // ------------------------------------------------------------ parser internals

    /// <summary>Flat key/value bookkeeping (no nested mappings in this format); unknown keys are tolerated (forward compatibility, ADR-014).</summary>
    private static bool TryParseBookkeeping(List<string> lines, out Dictionary<string, string> fields, out string error)
    {
        fields = new Dictionary<string, string>(StringComparer.Ordinal);
        error = string.Empty;

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                error = $"malformed bookkeeping line: '{line}'";
                return false;
            }

            fields[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        return true;
    }

    private static bool TryGetInt(Dictionary<string, string> fields, string key, out int value)
    {
        value = -1;
        return fields.TryGetValue(key, out var raw) &&
               int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetTimestamp(Dictionary<string, string> fields, string key, out DateTimeOffset value)
    {
        value = default;
        return fields.TryGetValue(key, out var raw) &&
               DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    private static bool TryGetNullableString(Dictionary<string, string> fields, string key, out string? value)
    {
        value = null;
        if (!fields.TryGetValue(key, out var raw) || raw == "null")
        {
            return true;
        }

        if (raw.StartsWith('"'))
        {
            try
            {
                value = JsonSerializer.Deserialize<string>(raw);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        value = raw;
        return true;
    }

    /// <summary>Finds the offset and kind of the next line that is exactly one of the entry sentinels, at or after <paramref name="from"/>.</summary>
    private static (int Position, string? Sentinel) FindSentinelLineStart(string content, int from)
    {
        var best = (-1, (string?)null);
        foreach (var sentinel in _sentinels)
        {
            var searchFrom = from;
            while (searchFrom <= content.Length - sentinel.Length)
            {
                var idx = content.IndexOf(sentinel, searchFrom, StringComparison.Ordinal);
                if (idx < 0)
                {
                    break;
                }

                var atLineStart = idx == 0 || content[idx - 1] == '\n';
                var lineEnd = idx + sentinel.Length;
                var atLineEnd = lineEnd == content.Length || content[lineEnd] == '\n';
                if (atLineStart && atLineEnd)
                {
                    if (best.Item1 < 0 || idx < best.Item1)
                    {
                        best = (idx, sentinel);
                    }

                    break;
                }

                searchFrom = idx + 1;
            }
        }

        return best;
    }

    private static bool TryReadLine(string content, ref int pos, out string line)
    {
        line = string.Empty;
        if (pos >= content.Length)
        {
            return false;
        }

        var newline = content.IndexOf('\n', pos);
        if (newline < 0)
        {
            line = content[pos..];
            pos = content.Length;
            return true;
        }

        line = content[pos..newline];
        pos = newline + 1;
        return true;
    }

    private static bool TryExpectLine(string content, ref int pos, string expected)
        => TryReadLine(content, ref pos, out var line) && line == expected;

    /// <summary>Length-delimited body slice: exactly <paramref name="length"/> UTF-16 code units followed by a newline.</summary>
    private static bool TrySliceBody(string content, ref int pos, int length, out string body)
    {
        body = string.Empty;

        if (length < 0 || pos + length >= content.Length || content[pos + length] != '\n')
        {
            return false;
        }

        body = content.Substring(pos, length);
        pos += length + 1;
        return true;
    }
}
