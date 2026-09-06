using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Grimoire.Hub.QueryConversations;

/// <summary>
/// Result of parsing a Conversation Record file
/// (contracts/conversation-record-format.md "Parsing").
/// </summary>
public abstract record QueryConversationRecordParseResult
{
    /// <summary>
    /// All complete turn blocks, in file order. <see cref="DroppedTrailingFragment"/> is
    /// true when a trailing incomplete block (crash mid-append) was dropped — the file
    /// is still readable, but the caller must emit a WARN diagnostic (Parsing rule 4).
    /// </summary>
    public sealed record Parsed(IReadOnlyList<QueryRecordedTurn> Turns, bool DroppedTrailingFragment)
        : QueryConversationRecordParseResult;

    /// <summary>
    /// Structural violation (bad frontmatter, malformed bookkeeping, body shorter than
    /// declared length) — the record is unreadable and context loading MUST fail closed
    /// (Parsing rule 5, FR-006).
    /// </summary>
    public sealed record Unreadable(string Reason) : QueryConversationRecordParseResult;
}

/// <summary>
/// Writer and parser for the <c>grimoire-conversation/1</c> Conversation Record format
/// (contracts/conversation-record-format.md). Bodies are <b>length-delimited, never
/// sentinel-delimited</b>: the parser slices prompt/answer bodies by their declared
/// UTF-16 code-unit lengths and never scans body content for headings or comment
/// markers, so untrusted LLM output containing <c>## Turn</c>, <c>### Answer</c>, or
/// <c>&lt;!-- grimoire:turn --&gt;</c> cannot break or forge structure (research.md R2).
/// String values inside the bookkeeping comment are JSON-escaped with <c>--&gt;</c>
/// neutralized, so agent-chosen content can never terminate the comment early.
/// </summary>
public static class QueryConversationRecordFormat
{
    public const string RecordFormatVersion = "grimoire-conversation/1";
    public const string TurnSentinel = "<!-- grimoire:turn";
    private const string CommentClose = "-->";

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Encoding used for record files (UTF-8, no BOM — the parser slices by char offsets).</summary>
    public static Encoding Encoding => _utf8NoBom;

    // ------------------------------------------------------------------ writer

    /// <summary>Frontmatter + document heading, written once when the record is created.</summary>
    public static string BuildRecordHeader(string conversationId, DateTimeOffset createdAt)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("conversation_id: ").Append(conversationId).Append('\n');
        sb.Append("created_at: ").Append(createdAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("record_format: ").Append(RecordFormatVersion).Append('\n');
        sb.Append("---\n");
        sb.Append('\n');
        sb.Append("# Conversation ").Append(conversationId).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// One complete, self-contained turn block (contract "Turn block grammar"):
    /// bookkeeping comment, turn heading, verbatim prompt/answer bodies with their
    /// exact UTF-16 code-unit lengths declared in <c>prompt_chars</c>/<c>answer_chars</c>.
    /// </summary>
    public static string BuildTurnBlock(QueryRecordedTurn turn)
    {
        var sb = new StringBuilder();
        sb.Append(TurnSentinel).Append('\n');
        sb.Append("turn_id: ").Append(turn.TurnId).Append('\n');
        sb.Append("position: ").Append(turn.Position).Append('\n');
        sb.Append("state: ").Append(turn.State).Append('\n');
        sb.Append("failure_reason: ").Append(NullableString(turn.FailureReason)).Append('\n');
        sb.Append("started_at: ").Append(turn.StartedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("completed_at: ").Append(turn.CompletedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "null").Append('\n');
        sb.Append("model: ").Append(NullableString(turn.Model)).Append('\n');
        sb.Append("turns_used: ").Append(turn.TurnsUsed?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('\n');
        sb.Append("foundation_file:\n");
        sb.Append("  path: ").Append(NullableString(turn.FoundationFilePath)).Append('\n');
        sb.Append("  sha256: ").Append(NullableString(turn.FoundationFileSha256)).Append('\n');
        sb.Append("instruction_file:\n");
        sb.Append("  path: ").Append(NullableString(turn.InstructionFilePath)).Append('\n');
        sb.Append("  sha256: ").Append(NullableString(turn.InstructionFileSha256)).Append('\n');
        sb.Append("policy:\n");
        sb.Append("  path: ").Append(NullableString(turn.PolicyPath)).Append('\n');
        sb.Append("  version: ").Append(turn.PolicyVersion?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('\n');
        sb.Append("  sha256: ").Append(NullableString(turn.PolicySha256)).Append('\n');
        if (turn.DeniedActions.Count == 0)
        {
            sb.Append("denied_actions: []\n");
        }
        else
        {
            sb.Append("denied_actions:\n");
            foreach (var denial in turn.DeniedActions)
            {
                sb.Append("  - action: ").Append(EscapeString(denial.Action)).Append('\n');
                sb.Append("    requested_target: ").Append(EscapeString(denial.RequestedTarget)).Append('\n');
                sb.Append("    canonical_target: ").Append(EscapeString(denial.CanonicalTarget)).Append('\n');
                sb.Append("    reason: ").Append(EscapeString(denial.Reason)).Append('\n');
                sb.Append("    turn: ").Append(denial.Turn).Append('\n');
            }
        }

        // ADR-015 (012-query-synthesis-writes): always present, never omitted — a turn
        // that created nothing records an explicit empty list, so parsers never need to
        // distinguish "no field" from "no pages" (contract §5).
        if (turn.CreatedPagesOrEmpty.Count == 0)
        {
            sb.Append("created_pages: []\n");
        }
        else
        {
            sb.Append("created_pages:\n");
            foreach (var page in turn.CreatedPagesOrEmpty)
            {
                sb.Append("  - ").Append(EscapeString(page)).Append('\n');
            }
        }

        sb.Append("prompt_chars: ").Append(turn.Prompt.Length).Append('\n');
        sb.Append("answer_chars: ").Append(turn.Answer.Length).Append('\n');
        sb.Append(CommentClose).Append('\n');
        sb.Append('\n');
        sb.Append("## Turn ").Append(turn.Position).Append(" — ").Append(turn.State).Append('\n');
        sb.Append('\n');
        sb.Append("### Prompt\n");
        sb.Append('\n');
        sb.Append(turn.Prompt).Append('\n');
        sb.Append('\n');
        sb.Append("### Answer\n");
        sb.Append('\n');
        sb.Append(turn.Answer).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }

    private static string NullableString(string? value) => value is null ? "null" : EscapeString(value);

    /// <summary>
    /// Double-quoted JSON-escaped string. The default JSON encoder escapes
    /// <c>&gt;</c> as <c>></c>, so the sequence <c>--&gt;</c> can never appear
    /// literally inside the bookkeeping comment (contract "Escaping rules"); an
    /// explicit guard below makes that invariant fail loudly rather than silently
    /// should the serializer behavior ever change.
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
    /// Parses a record back into its complete turns (contract "Parsing", rules 1–5).
    /// Sentinel scanning happens strictly outside length-consumed body ranges; unknown
    /// bookkeeping keys are tolerated (feature-012 forward compatibility); a trailing
    /// incomplete block is dropped (readable, flagged); any other structural violation
    /// classifies the record as unreadable.
    /// </summary>
    public static QueryConversationRecordParseResult Parse(string content)
    {
        var pos = 0;

        // Rule 1: frontmatter with the exact record_format handshake.
        if (!TryParseFrontmatter(content, ref pos, out var frontmatterError))
        {
            return new QueryConversationRecordParseResult.Unreadable(frontmatterError);
        }

        // Rule 2: scan for turn sentinels strictly outside body ranges — bodies below are
        // consumed by declared length, and scanning resumes only after each block's
        // trailing newline.
        var turns = new List<QueryRecordedTurn>();
        var droppedTrailingFragment = false;

        while (true)
        {
            var sentinelPos = FindSentinelLineStart(content, pos);
            if (sentinelPos < 0)
            {
                break;
            }

            pos = sentinelPos;
            var block = ParseTurnBlock(content, ref pos);
            if (block is TurnBlockResult.Unreadable unreadable)
            {
                return new QueryConversationRecordParseResult.Unreadable(unreadable.Reason);
            }

            if (block is not TurnBlockResult.Parsed parsedBlock)
            {
                // Rule 4: trailing incomplete block (crash mid-append) — drop the
                // fragment; the recorded prefix is exactly the fully recorded turns.
                droppedTrailingFragment = true;
                break;
            }

            turns.Add(parsedBlock.Turn);
        }

        return new QueryConversationRecordParseResult.Parsed(turns, droppedTrailingFragment);
    }

    // ------------------------------------------------------------ parser internals

    /// <summary>
    /// Outcome of reading a single turn block: a complete turn, a crash-truncated tail that
    /// is dropped and flagged (Parsing rule 4), or a structural violation carrying the
    /// contract reason that makes the whole record unreadable (Parsing rule 5).
    /// </summary>
    private abstract record TurnBlockResult
    {
        public sealed record Parsed(QueryRecordedTurn Turn) : TurnBlockResult { }

        public sealed record TrailingFragment : TurnBlockResult { }

        public sealed record Unreadable(string Reason) : TurnBlockResult { }
    }

    /// <summary>
    /// Rule 1: the frontmatter block and its exact <c>record_format</c> handshake. On
    /// failure <paramref name="error"/> carries the contract's unreadable reason.
    /// </summary>
    private static bool TryParseFrontmatter(string content, ref int pos, out string error)
    {
        error = string.Empty;

        if (!TryReadLine(content, ref pos, out var line) || line != "---")
        {
            error = "missing frontmatter opening '---'";
            return false;
        }

        var frontmatter = new Dictionary<string, string>(StringComparer.Ordinal);
        while (true)
        {
            if (!TryReadLine(content, ref pos, out line))
            {
                error = "truncated frontmatter (no closing '---')";
                return false;
            }

            if (line == "---")
            {
                break;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                error = $"malformed frontmatter line: '{line}'";
                return false;
            }

            frontmatter[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        if (!frontmatter.TryGetValue("record_format", out var format) || format != RecordFormatVersion)
        {
            error = $"unsupported record_format '{frontmatter.GetValueOrDefault("record_format", "(missing)")}'";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the one turn block whose sentinel line starts at <paramref name="pos"/>: the
    /// bookkeeping comment, the structural lines, and the two length-delimited bodies.
    /// <paramref name="pos"/> is left after the block's trailing blank line, from where
    /// sentinel scanning safely resumes.
    /// </summary>
    private static TurnBlockResult ParseTurnBlock(string content, ref int pos)
    {
        if (!TryReadBookkeepingComment(content, ref pos, out var bookkeepingLines))
        {
            return new TurnBlockResult.TrailingFragment();
        }

        if (!TryParseBookkeeping(bookkeepingLines, out var bookkeeping, out var bookkeepingError))
        {
            return new TurnBlockResult.Unreadable($"malformed turn bookkeeping: {bookkeepingError}");
        }

        // Structural lines between the comment and the length-delimited bodies.
        if (!TryExpectLine(content, ref pos, string.Empty))
        {
            return new TurnBlockResult.Unreadable("expected blank line after bookkeeping comment");
        }

        var expectedHeading = $"## Turn {bookkeeping.Position} — {bookkeeping.State}";
        if (!TryReadLine(content, ref pos, out var heading) || heading != expectedHeading)
        {
            return new TurnBlockResult.Unreadable(
                $"missing or mismatched turn heading (expected '{expectedHeading}')");
        }

        if (!TryExpectSection(content, ref pos, "### Prompt"))
        {
            return new TurnBlockResult.Unreadable("malformed prompt section structure");
        }

        if (!TrySliceBody(content, ref pos, bookkeeping.PromptChars, out var prompt))
        {
            return new TurnBlockResult.Unreadable(
                "prompt body shorter than declared prompt_chars length");
        }

        if (!TryExpectSection(content, ref pos, "### Answer"))
        {
            return new TurnBlockResult.Unreadable("malformed answer section structure");
        }

        if (!TrySliceBody(content, ref pos, bookkeeping.AnswerChars, out var answer))
        {
            return new TurnBlockResult.Unreadable(
                "answer body shorter than declared answer_chars length");
        }

        if (!TryExpectLine(content, ref pos, string.Empty))
        {
            return new TurnBlockResult.Unreadable("expected blank line after answer body");
        }

        return new TurnBlockResult.Parsed(new QueryRecordedTurn(
            TurnId: bookkeeping.TurnId!,
            Position: bookkeeping.Position,
            State: bookkeeping.State!,
            FailureReason: bookkeeping.FailureReason,
            StartedAt: bookkeeping.StartedAt,
            CompletedAt: bookkeeping.CompletedAt,
            Model: bookkeeping.Model,
            TurnsUsed: bookkeeping.TurnsUsed,
            FoundationFilePath: bookkeeping.FoundationFilePath,
            FoundationFileSha256: bookkeeping.FoundationFileSha256,
            InstructionFilePath: bookkeeping.InstructionFilePath,
            InstructionFileSha256: bookkeeping.InstructionFileSha256,
            PolicyPath: bookkeeping.PolicyPath,
            PolicyVersion: bookkeeping.PolicyVersion,
            PolicySha256: bookkeeping.PolicySha256,
            DeniedActions: bookkeeping.DeniedActions,
            Prompt: prompt,
            Answer: answer,
            CreatedPages: bookkeeping.CreatedPages));
    }

    /// <summary>
    /// Consumes the sentinel line and the bookkeeping comment body up to its closing
    /// <c>--&gt;</c>. Returns false when the comment is never closed — the crash-truncated
    /// trailing block of Parsing rule 4.
    /// </summary>
    private static bool TryReadBookkeepingComment(string content, ref int pos, out List<string> lines)
    {
        TryReadLine(content, ref pos, out _); // consume the sentinel line itself

        lines = [];
        while (TryReadLine(content, ref pos, out var line))
        {
            if (line == CommentClose)
            {
                return true;
            }

            lines.Add(line);
        }

        return false;
    }

    /// <summary>Blank line, section heading, blank line — the preamble of a body section.</summary>
    private static bool TryExpectSection(string content, ref int pos, string heading)
        => TryExpectLine(content, ref pos, string.Empty)
            && TryExpectLine(content, ref pos, heading)
            && TryExpectLine(content, ref pos, string.Empty);

    private sealed class ParsedBookkeeping
    {
        public string? TurnId;
        public int Position = -1;
        public string? State;
        public string? FailureReason;
        public DateTimeOffset StartedAt;
        public bool HasStartedAt;
        public DateTimeOffset? CompletedAt;
        public string? Model;
        public int? TurnsUsed;
        public string? FoundationFilePath;
        public string? FoundationFileSha256;
        public string? InstructionFilePath;
        public string? InstructionFileSha256;
        public string? PolicyPath;
        public int? PolicyVersion;
        public string? PolicySha256;
        public List<QueryRecordedDeniedAction> DeniedActions = [];
        // ADR-015 (012-query-synthesis-writes): absent on records predating this feature —
        // stays the default empty list (forward-compat, ADR-014), tolerated by the
        // "unknown key" dictionary-miss path below for old records that omit the key entirely.
        public List<string> CreatedPages = [];
        public int PromptChars = -1;
        public int AnswerChars = -1;
    }

    /// <summary>
    /// Scan position within one turn's bookkeeping lines, plus the failure reason recorded
    /// by the field parsers. Nested mappings and block lists consume their own continuation
    /// lines, so the position is shared between the key loop and the field parsers.
    /// </summary>
    private sealed class BookkeepingCursor(List<string> lines)
    {
        public List<string> Lines { get; } = lines;

        public int Index { get; set; }

        public string Error { get; private set; } = string.Empty;

        /// <summary>Records <paramref name="reason"/> and returns false, so a failing parser can <c>return cursor.Fail(...)</c>.</summary>
        public bool Fail(string reason)
        {
            Error = reason;
            return false;
        }
    }

    /// <summary>
    /// Parses the value of one bookkeeping key into <paramref name="result"/>.
    /// <paramref name="raw"/> is the trimmed scalar after the colon; parsers of nested
    /// mappings and block lists additionally consume their continuation lines from
    /// <paramref name="cursor"/>, and every parser reports failure through it.
    /// </summary>
    private delegate bool BookkeepingFieldParser(ParsedBookkeeping result, string raw, BookkeepingCursor cursor);

    /// <summary>
    /// The recognized bookkeeping keys, in write order (<see cref="BuildTurnBlock"/>). A key
    /// that is missing here is an unknown key — tolerated for forward compatibility, never
    /// an error (feature-012, ADR-014).
    /// </summary>
    private static readonly Dictionary<string, BookkeepingFieldParser> _bookkeepingFields =
        new(StringComparer.Ordinal)
        {
            ["turn_id"] = ParseTurnId,
            ["position"] = ParsePosition,
            ["state"] = ParseState,
            ["failure_reason"] = ParseFailureReason,
            ["started_at"] = ParseStartedAt,
            ["completed_at"] = ParseCompletedAt,
            ["model"] = ParseModel,
            ["turns_used"] = ParseTurnsUsed,
            ["foundation_file"] = ParseFoundationFile,
            ["instruction_file"] = ParseInstructionFile,
            ["policy"] = ParsePolicy,
            ["denied_actions"] = ParseDeniedActions,
            ["created_pages"] = ParseCreatedPages,
            ["prompt_chars"] = ParsePromptChars,
            ["answer_chars"] = ParseAnswerChars,
        };

    private static bool TryParseBookkeeping(List<string> lines, out ParsedBookkeeping result, out string error)
    {
        result = new ParsedBookkeeping();
        var cursor = new BookkeepingCursor(lines);

        var parsed = TryParseBookkeepingFields(result, cursor) && HasRequiredFields(result, cursor);

        error = cursor.Error;
        return parsed;
    }

    /// <summary>Key loop: one line per key, each dispatched to its field parser.</summary>
    private static bool TryParseBookkeepingFields(ParsedBookkeeping result, BookkeepingCursor cursor)
    {
        while (cursor.Index < cursor.Lines.Count)
        {
            var line = cursor.Lines[cursor.Index];
            if (line.Length == 0)
            {
                cursor.Index++;
                continue;
            }

            if (line.StartsWith(' '))
            {
                return cursor.Fail($"unexpected indented line outside a nested mapping: '{line}'");
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return cursor.Fail($"malformed bookkeeping line: '{line}'");
            }

            var key = line[..colon];
            var raw = line[(colon + 1)..].Trim();
            cursor.Index++;

            if (!_bookkeepingFields.TryGetValue(key, out var parseField))
            {
                // Unknown key — tolerated (forward compatibility, e.g. feature 012's
                // created_pages). Skip any nested/indented continuation lines.
                SkipUnknownKeyContinuation(cursor);
                continue;
            }

            if (!parseField(result, raw, cursor))
            {
                return false;
            }
        }

        return true;
    }

    private static void SkipUnknownKeyContinuation(BookkeepingCursor cursor)
    {
        while (cursor.Index < cursor.Lines.Count &&
               (cursor.Lines[cursor.Index].Length == 0 || cursor.Lines[cursor.Index].StartsWith(' ')))
        {
            cursor.Index++;
        }
    }

    private static bool HasRequiredFields(ParsedBookkeeping result, BookkeepingCursor cursor)
        => (result.TurnId is not null && result.State is not null && result.HasStartedAt &&
            result.Position >= 0 && result.PromptChars >= 0 && result.AnswerChars >= 0)
            || cursor.Fail(
                "missing required bookkeeping field (turn_id, position, state, started_at, prompt_chars, answer_chars)");

    // -------------------------------------------------------- bookkeeping field parsers

    private static bool ParseTurnId(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
    {
        result.TurnId = raw;
        return true;
    }

    private static bool ParsePosition(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseInt(raw, out result.Position, cursor, "position is not a non-negative integer");

    private static bool ParseState(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
    {
        result.State = raw;
        return true;
    }

    private static bool ParseFailureReason(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseNullableString(raw, out result.FailureReason, cursor);

    private static bool ParseStartedAt(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
    {
        if (!TryParseTimestamp(raw, out result.StartedAt, cursor, "started_at is not a valid timestamp"))
        {
            return false;
        }

        result.HasStartedAt = true;
        return true;
    }

    private static bool ParseCompletedAt(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseNullableTimestamp(raw, out result.CompletedAt, cursor, "completed_at is not a valid timestamp");

    private static bool ParseModel(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseNullableString(raw, out result.Model, cursor);

    private static bool ParseTurnsUsed(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseNullableInt(raw, out result.TurnsUsed, cursor, "turns_used is not an integer");

    private static bool ParsePromptChars(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseInt(raw, out result.PromptChars, cursor, "prompt_chars is not a non-negative integer");

    private static bool ParseAnswerChars(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseInt(raw, out result.AnswerChars, cursor, "answer_chars is not a non-negative integer");

    private static bool ParseFoundationFile(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseNestedMapping(cursor, out var nested)
            && TryGetNullableString(nested, "path", out result.FoundationFilePath, cursor)
            && TryGetNullableString(nested, "sha256", out result.FoundationFileSha256, cursor);

    private static bool ParseInstructionFile(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseNestedMapping(cursor, out var nested)
            && TryGetNullableString(nested, "path", out result.InstructionFilePath, cursor)
            && TryGetNullableString(nested, "sha256", out result.InstructionFileSha256, cursor);

    private static bool ParsePolicy(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryParseNestedMapping(cursor, out var nested)
            && TryGetNullableString(nested, "path", out result.PolicyPath, cursor)
            && TryGetNullableString(nested, "sha256", out result.PolicySha256, cursor)
            && TryGetNullableInt(nested, "version", out result.PolicyVersion, cursor, "policy version is not an integer");

    private static bool ParseDeniedActions(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryReadBlockListHeader(raw, "denied_actions", cursor, out var hasEntries)
            && (!hasEntries || TryParseDeniedActionEntries(cursor, result.DeniedActions));

    /// <summary>
    /// ADR-015 (012-query-synthesis-writes): '[]' or a flat block list of JSON-escaped
    /// strings — the same grammar shape as denied_actions, but each entry is a bare string
    /// rather than a nested mapping.
    /// </summary>
    private static bool ParseCreatedPages(ParsedBookkeeping result, string raw, BookkeepingCursor cursor)
        => TryReadBlockListHeader(raw, "created_pages", cursor, out var hasEntries)
            && (!hasEntries || TryParseStringList(cursor, result.CreatedPages));

    // ------------------------------------------------- shared bookkeeping value parsers

    /// <summary>
    /// The scalar of a list-valued key: <c>[]</c> for the empty inline form, or an empty
    /// scalar introducing a block list — in which case <paramref name="hasEntries"/> is true
    /// and the caller consumes the entry lines that follow.
    /// </summary>
    private static bool TryReadBlockListHeader(
        string raw, string key, BookkeepingCursor cursor, out bool hasEntries)
    {
        hasEntries = false;
        if (raw == "[]")
        {
            return true;
        }

        if (raw.Length != 0)
        {
            return cursor.Fail($"{key} must be '[]' or a block list, got: '{raw}'");
        }

        hasEntries = true;
        return true;
    }

    private static bool TryParseNestedMapping(BookkeepingCursor cursor, out Dictionary<string, string> nested)
    {
        nested = new Dictionary<string, string>(StringComparer.Ordinal);
        while (cursor.Index < cursor.Lines.Count &&
               cursor.Lines[cursor.Index].StartsWith("  ", StringComparison.Ordinal))
        {
            var line = cursor.Lines[cursor.Index].TrimStart();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return cursor.Fail($"malformed nested mapping line: '{cursor.Lines[cursor.Index]}'");
            }

            nested[line[..colon]] = line[(colon + 1)..].Trim();
            cursor.Index++;
        }

        return true;
    }

    private static bool TryParseDeniedActionEntries(
        BookkeepingCursor cursor, List<QueryRecordedDeniedAction> deniedActions)
    {
        while (cursor.Index < cursor.Lines.Count &&
               cursor.Lines[cursor.Index].StartsWith("  - ", StringComparison.Ordinal))
        {
            if (!TryParseDeniedActionEntry(cursor, out var entry))
            {
                return false;
            }

            if (!TryGetNullableString(entry, "action", out var action, cursor) ||
                !TryGetNullableString(entry, "requested_target", out var requestedTarget, cursor) ||
                !TryGetNullableString(entry, "canonical_target", out var canonicalTarget, cursor) ||
                !TryGetNullableString(entry, "reason", out var reason, cursor))
            {
                return false;
            }

            if (!entry.TryGetValue("turn", out var turnRaw) ||
                !int.TryParse(turnRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var turn))
            {
                return cursor.Fail("denied_actions entry is missing a valid 'turn' field");
            }

            deniedActions.Add(new QueryRecordedDeniedAction(
                action ?? string.Empty,
                requestedTarget ?? string.Empty,
                canonicalTarget ?? string.Empty,
                reason ?? string.Empty,
                turn));
        }

        return true;
    }

    /// <summary>One <c>  - key: value</c> entry plus its <c>    key: value</c> continuation lines.</summary>
    private static bool TryParseDeniedActionEntry(BookkeepingCursor cursor, out Dictionary<string, string> entry)
    {
        entry = new Dictionary<string, string>(StringComparer.Ordinal);

        var firstLine = cursor.Lines[cursor.Index][4..];
        var colon = firstLine.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return cursor.Fail($"malformed denied_actions entry line: '{cursor.Lines[cursor.Index]}'");
        }

        entry[firstLine[..colon]] = firstLine[(colon + 1)..].Trim();
        cursor.Index++;

        while (cursor.Index < cursor.Lines.Count &&
               cursor.Lines[cursor.Index].StartsWith("    ", StringComparison.Ordinal))
        {
            var line = cursor.Lines[cursor.Index][4..];
            colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return cursor.Fail($"malformed denied_actions field line: '{cursor.Lines[cursor.Index]}'");
            }

            entry[line[..colon]] = line[(colon + 1)..].Trim();
            cursor.Index++;
        }

        return true;
    }

    /// <summary>Parses a flat `  - "value"` block list (created_pages) into <paramref name="target"/>.</summary>
    private static bool TryParseStringList(BookkeepingCursor cursor, List<string> target)
    {
        while (cursor.Index < cursor.Lines.Count &&
               cursor.Lines[cursor.Index].StartsWith("  - ", StringComparison.Ordinal))
        {
            if (!TryParseNullableString(cursor.Lines[cursor.Index][4..], out var value, cursor))
            {
                return false;
            }

            target.Add(value ?? string.Empty);
            cursor.Index++;
        }

        return true;
    }

    private static bool TryGetNullableString(
        Dictionary<string, string> mapping, string key, out string? value, BookkeepingCursor cursor)
    {
        value = null;
        if (!mapping.TryGetValue(key, out var raw))
        {
            return true;
        }

        return TryParseNullableString(raw, out value, cursor);
    }

    private static bool TryGetNullableInt(
        Dictionary<string, string> mapping, string key, out int? value, BookkeepingCursor cursor, string expectation)
    {
        value = null;
        if (!mapping.TryGetValue(key, out var raw))
        {
            return true;
        }

        return TryParseNullableInt(raw, out value, cursor, expectation);
    }

    private static bool TryParseNullableString(string raw, out string? value, BookkeepingCursor cursor)
    {
        if (raw == "null")
        {
            value = null;
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
                value = null;
                return cursor.Fail($"malformed JSON string scalar: '{raw}'");
            }
        }

        value = raw;
        return true;
    }

    /// <summary>
    /// Non-negative integer scalar. On failure the recorded reason is
    /// "<paramref name="expectation"/>: '&lt;raw&gt;'".
    /// </summary>
    private static bool TryParseInt(string raw, out int value, BookkeepingCursor cursor, string expectation)
        => int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value)
            || cursor.Fail($"{expectation}: '{raw}'");

    /// <summary>Integer scalar, or the literal <c>null</c>.</summary>
    private static bool TryParseNullableInt(string raw, out int? value, BookkeepingCursor cursor, string expectation)
    {
        value = null;
        if (raw == "null")
        {
            return true;
        }

        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return cursor.Fail($"{expectation}: '{raw}'");
        }

        value = parsed;
        return true;
    }

    /// <summary>Round-trip ("O") timestamp scalar.</summary>
    private static bool TryParseTimestamp(
        string raw, out DateTimeOffset value, BookkeepingCursor cursor, string expectation)
        => DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value)
            || cursor.Fail($"{expectation}: '{raw}'");

    /// <summary>Round-trip ("O") timestamp scalar, or the literal <c>null</c>.</summary>
    private static bool TryParseNullableTimestamp(
        string raw, out DateTimeOffset? value, BookkeepingCursor cursor, string expectation)
    {
        value = null;
        if (raw == "null")
        {
            return true;
        }

        if (!TryParseTimestamp(raw, out var parsed, cursor, expectation))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    // ------------------------------------------------------------------ text scanning

    /// <summary>Finds the offset of the next line that is exactly the turn sentinel, at or after <paramref name="from"/>.</summary>
    private static int FindSentinelLineStart(string content, int from)
    {
        var searchFrom = from;
        while (searchFrom <= content.Length - TurnSentinel.Length)
        {
            var idx = content.IndexOf(TurnSentinel, searchFrom, StringComparison.Ordinal);
            if (idx < 0)
            {
                return -1;
            }

            var atLineStart = idx == 0 || content[idx - 1] == '\n';
            var lineEnd = idx + TurnSentinel.Length;
            var atLineEnd = lineEnd == content.Length || content[lineEnd] == '\n';
            if (atLineStart && atLineEnd)
            {
                return idx;
            }

            searchFrom = idx + 1;
        }

        return -1;
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

        // The body plus its mandatory trailing newline must fit in the remaining file:
        // anything shorter than the declared length is a structural violation (rule 5).
        if (length < 0 || pos + length >= content.Length || content[pos + length] != '\n')
        {
            return false;
        }

        body = content.Substring(pos, length);
        pos += length + 1;
        return true;
    }
}
