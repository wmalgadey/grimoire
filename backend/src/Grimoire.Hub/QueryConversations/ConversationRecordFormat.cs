using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Grimoire.Hub.QueryConversations;

/// <summary>
/// Result of parsing a Conversation Record file
/// (contracts/conversation-record-format.md "Parsing").
/// </summary>
public abstract record ConversationRecordParseResult
{
    /// <summary>
    /// All complete turn blocks, in file order. <see cref="DroppedTrailingFragment"/> is
    /// true when a trailing incomplete block (crash mid-append) was dropped — the file
    /// is still readable, but the caller must emit a WARN diagnostic (Parsing rule 4).
    /// </summary>
    public sealed record Parsed(IReadOnlyList<RecordedTurn> Turns, bool DroppedTrailingFragment)
        : ConversationRecordParseResult;

    /// <summary>
    /// Structural violation (bad frontmatter, malformed bookkeeping, body shorter than
    /// declared length) — the record is unreadable and context loading MUST fail closed
    /// (Parsing rule 5, FR-006).
    /// </summary>
    public sealed record Unreadable(string Reason) : ConversationRecordParseResult;
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
public static class ConversationRecordFormat
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
    public static string BuildTurnBlock(RecordedTurn turn)
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
    public static ConversationRecordParseResult Parse(string content)
    {
        var pos = 0;

        // Rule 1: frontmatter with the exact record_format handshake.
        if (!TryReadLine(content, ref pos, out var line) || line != "---")
        {
            return new ConversationRecordParseResult.Unreadable("missing frontmatter opening '---'");
        }

        var frontmatter = new Dictionary<string, string>(StringComparer.Ordinal);
        while (true)
        {
            if (!TryReadLine(content, ref pos, out line))
            {
                return new ConversationRecordParseResult.Unreadable("truncated frontmatter (no closing '---')");
            }

            if (line == "---")
            {
                break;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return new ConversationRecordParseResult.Unreadable($"malformed frontmatter line: '{line}'");
            }

            frontmatter[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        if (!frontmatter.TryGetValue("record_format", out var format) || format != RecordFormatVersion)
        {
            return new ConversationRecordParseResult.Unreadable(
                $"unsupported record_format '{frontmatter.GetValueOrDefault("record_format", "(missing)")}'");
        }

        // Rule 2: scan for turn sentinels strictly outside body ranges — bodies below are
        // consumed by declared length, and scanning resumes only after each block's
        // trailing newline.
        var turns = new List<RecordedTurn>();
        var droppedTrailingFragment = false;

        while (true)
        {
            var sentinelPos = FindSentinelLineStart(content, pos);
            if (sentinelPos < 0)
            {
                break;
            }

            pos = sentinelPos;
            TryReadLine(content, ref pos, out _); // consume the sentinel line itself

            var bookkeepingLines = new List<string>();
            var closed = false;
            while (TryReadLine(content, ref pos, out line))
            {
                if (line == CommentClose)
                {
                    closed = true;
                    break;
                }

                bookkeepingLines.Add(line);
            }

            if (!closed)
            {
                // Rule 4: trailing incomplete block (crash mid-append) — drop the
                // fragment; the recorded prefix is exactly the fully recorded turns.
                droppedTrailingFragment = true;
                break;
            }

            if (!TryParseBookkeeping(bookkeepingLines, out var bookkeeping, out var bookkeepingError))
            {
                return new ConversationRecordParseResult.Unreadable($"malformed turn bookkeeping: {bookkeepingError}");
            }

            // Structural lines between the comment and the length-delimited bodies.
            if (!TryExpectLine(content, ref pos, string.Empty))
            {
                return new ConversationRecordParseResult.Unreadable("expected blank line after bookkeeping comment");
            }

            var expectedHeading = $"## Turn {bookkeeping.Position} — {bookkeeping.State}";
            if (!TryReadLine(content, ref pos, out line) || line != expectedHeading)
            {
                return new ConversationRecordParseResult.Unreadable(
                    $"missing or mismatched turn heading (expected '{expectedHeading}')");
            }

            if (!TryExpectLine(content, ref pos, string.Empty) ||
                !TryExpectLine(content, ref pos, "### Prompt") ||
                !TryExpectLine(content, ref pos, string.Empty))
            {
                return new ConversationRecordParseResult.Unreadable("malformed prompt section structure");
            }

            if (!TrySliceBody(content, ref pos, bookkeeping.PromptChars, out var prompt))
            {
                return new ConversationRecordParseResult.Unreadable(
                    "prompt body shorter than declared prompt_chars length");
            }

            if (!TryExpectLine(content, ref pos, string.Empty) ||
                !TryExpectLine(content, ref pos, "### Answer") ||
                !TryExpectLine(content, ref pos, string.Empty))
            {
                return new ConversationRecordParseResult.Unreadable("malformed answer section structure");
            }

            if (!TrySliceBody(content, ref pos, bookkeeping.AnswerChars, out var answer))
            {
                return new ConversationRecordParseResult.Unreadable(
                    "answer body shorter than declared answer_chars length");
            }

            if (!TryExpectLine(content, ref pos, string.Empty))
            {
                return new ConversationRecordParseResult.Unreadable("expected blank line after answer body");
            }

            turns.Add(new RecordedTurn(
                TurnId: bookkeeping.TurnId!,
                Position: bookkeeping.Position,
                State: bookkeeping.State!,
                FailureReason: bookkeeping.FailureReason,
                StartedAt: bookkeeping.StartedAt,
                CompletedAt: bookkeeping.CompletedAt,
                Model: bookkeeping.Model,
                TurnsUsed: bookkeeping.TurnsUsed,
                InstructionFilePath: bookkeeping.InstructionFilePath,
                InstructionFileSha256: bookkeeping.InstructionFileSha256,
                PolicyPath: bookkeeping.PolicyPath,
                PolicyVersion: bookkeeping.PolicyVersion,
                PolicySha256: bookkeeping.PolicySha256,
                DeniedActions: bookkeeping.DeniedActions,
                Prompt: prompt,
                Answer: answer));
        }

        return new ConversationRecordParseResult.Parsed(turns, droppedTrailingFragment);
    }

    // ------------------------------------------------------------ parser internals

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
        public string? InstructionFilePath;
        public string? InstructionFileSha256;
        public string? PolicyPath;
        public int? PolicyVersion;
        public string? PolicySha256;
        public List<RecordedDeniedAction> DeniedActions = [];
        public int PromptChars = -1;
        public int AnswerChars = -1;
    }

    private static bool TryParseBookkeeping(List<string> lines, out ParsedBookkeeping result, out string error)
    {
        result = new ParsedBookkeeping();
        error = string.Empty;

        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                i++;
                continue;
            }

            if (line.StartsWith(' '))
            {
                error = $"unexpected indented line outside a nested mapping: '{line}'";
                return false;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                error = $"malformed bookkeeping line: '{line}'";
                return false;
            }

            var key = line[..colon];
            var raw = line[(colon + 1)..].Trim();
            i++;

            switch (key)
            {
                case "turn_id":
                    result.TurnId = raw;
                    break;
                case "position":
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out result.Position))
                    {
                        error = $"position is not a non-negative integer: '{raw}'";
                        return false;
                    }
                    break;
                case "state":
                    result.State = raw;
                    break;
                case "failure_reason":
                    if (!TryParseNullableString(raw, out result.FailureReason, ref error)) return false;
                    break;
                case "started_at":
                    if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result.StartedAt))
                    {
                        error = $"started_at is not a valid timestamp: '{raw}'";
                        return false;
                    }
                    result.HasStartedAt = true;
                    break;
                case "completed_at":
                    if (raw == "null")
                    {
                        result.CompletedAt = null;
                    }
                    else if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var completedAt))
                    {
                        result.CompletedAt = completedAt;
                    }
                    else
                    {
                        error = $"completed_at is not a valid timestamp: '{raw}'";
                        return false;
                    }
                    break;
                case "model":
                    if (!TryParseNullableString(raw, out result.Model, ref error)) return false;
                    break;
                case "turns_used":
                    if (raw == "null")
                    {
                        result.TurnsUsed = null;
                    }
                    else if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var turnsUsed))
                    {
                        result.TurnsUsed = turnsUsed;
                    }
                    else
                    {
                        error = $"turns_used is not an integer: '{raw}'";
                        return false;
                    }
                    break;
                case "prompt_chars":
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out result.PromptChars))
                    {
                        error = $"prompt_chars is not a non-negative integer: '{raw}'";
                        return false;
                    }
                    break;
                case "answer_chars":
                    if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out result.AnswerChars))
                    {
                        error = $"answer_chars is not a non-negative integer: '{raw}'";
                        return false;
                    }
                    break;
                case "instruction_file":
                {
                    if (!TryParseNestedMapping(lines, ref i, out var nested, ref error)) return false;
                    if (!TryGetNullableString(nested, "path", out result.InstructionFilePath, ref error)) return false;
                    if (!TryGetNullableString(nested, "sha256", out result.InstructionFileSha256, ref error)) return false;
                    break;
                }
                case "policy":
                {
                    if (!TryParseNestedMapping(lines, ref i, out var nested, ref error)) return false;
                    if (!TryGetNullableString(nested, "path", out result.PolicyPath, ref error)) return false;
                    if (!TryGetNullableString(nested, "sha256", out result.PolicySha256, ref error)) return false;
                    if (nested.TryGetValue("version", out var versionRaw))
                    {
                        if (versionRaw == "null")
                        {
                            result.PolicyVersion = null;
                        }
                        else if (int.TryParse(versionRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
                        {
                            result.PolicyVersion = version;
                        }
                        else
                        {
                            error = $"policy version is not an integer: '{versionRaw}'";
                            return false;
                        }
                    }
                    break;
                }
                case "denied_actions":
                {
                    if (raw == "[]")
                    {
                        break;
                    }

                    if (raw.Length != 0)
                    {
                        error = $"denied_actions must be '[]' or a block list, got: '{raw}'";
                        return false;
                    }

                    if (!TryParseDeniedActions(lines, ref i, result.DeniedActions, ref error)) return false;
                    break;
                }
                default:
                    // Unknown key — tolerated (forward compatibility, e.g. feature 012's
                    // created_pages). Skip any nested/indented continuation lines.
                    while (i < lines.Count && (lines[i].Length == 0 || lines[i].StartsWith(' ')))
                    {
                        i++;
                    }
                    break;
            }
        }

        if (result.TurnId is null || result.State is null || !result.HasStartedAt ||
            result.Position < 0 || result.PromptChars < 0 || result.AnswerChars < 0)
        {
            error = "missing required bookkeeping field (turn_id, position, state, started_at, prompt_chars, answer_chars)";
            return false;
        }

        return true;
    }

    private static bool TryParseNestedMapping(
        List<string> lines, ref int i, out Dictionary<string, string> nested, ref string error)
    {
        nested = new Dictionary<string, string>(StringComparer.Ordinal);
        while (i < lines.Count && lines[i].StartsWith("  ", StringComparison.Ordinal))
        {
            var line = lines[i].TrimStart();
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                error = $"malformed nested mapping line: '{lines[i]}'";
                return false;
            }

            nested[line[..colon]] = line[(colon + 1)..].Trim();
            i++;
        }

        return true;
    }

    private static bool TryParseDeniedActions(
        List<string> lines, ref int i, List<RecordedDeniedAction> deniedActions, ref string error)
    {
        while (i < lines.Count && lines[i].StartsWith("  - ", StringComparison.Ordinal))
        {
            var entry = new Dictionary<string, string>(StringComparer.Ordinal);

            var firstLine = lines[i][4..];
            var colon = firstLine.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                error = $"malformed denied_actions entry line: '{lines[i]}'";
                return false;
            }

            entry[firstLine[..colon]] = firstLine[(colon + 1)..].Trim();
            i++;

            while (i < lines.Count && lines[i].StartsWith("    ", StringComparison.Ordinal))
            {
                var line = lines[i][4..];
                colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0)
                {
                    error = $"malformed denied_actions field line: '{lines[i]}'";
                    return false;
                }

                entry[line[..colon]] = line[(colon + 1)..].Trim();
                i++;
            }

            if (!TryGetNullableString(entry, "action", out var action, ref error) ||
                !TryGetNullableString(entry, "requested_target", out var requestedTarget, ref error) ||
                !TryGetNullableString(entry, "canonical_target", out var canonicalTarget, ref error) ||
                !TryGetNullableString(entry, "reason", out var reason, ref error))
            {
                return false;
            }

            if (!entry.TryGetValue("turn", out var turnRaw) ||
                !int.TryParse(turnRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var turn))
            {
                error = "denied_actions entry is missing a valid 'turn' field";
                return false;
            }

            deniedActions.Add(new RecordedDeniedAction(
                action ?? string.Empty,
                requestedTarget ?? string.Empty,
                canonicalTarget ?? string.Empty,
                reason ?? string.Empty,
                turn));
        }

        return true;
    }

    private static bool TryGetNullableString(
        Dictionary<string, string> mapping, string key, out string? value, ref string error)
    {
        value = null;
        if (!mapping.TryGetValue(key, out var raw))
        {
            return true;
        }

        return TryParseNullableString(raw, out value, ref error);
    }

    private static bool TryParseNullableString(string raw, out string? value, ref string error)
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
                error = $"malformed JSON string scalar: '{raw}'";
                value = null;
                return false;
            }
        }

        value = raw;
        return true;
    }

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
