using System.Text.Json;
using Grimoire.AgentRuntime.Core.Adapters.Replay;

namespace Grimoire.EvalRunner.Recording;

/// <summary>How much page content one recorded run read, and through which read shapes.</summary>
public sealed record ReadAccounting(int ContentTokens, int FullReads, int RangedReads, int FrontmatterReads)
{
    public int TotalReads => FullReads + RangedReads + FrontmatterReads;
}

/// <summary>
/// T066/T069 (026-guarded-tool-surface): reconstructs "how much page content did this run
/// read" from a sample recording plus the fixture it ran against.
///
/// <para><b>Why not peak <c>input_tokens</c>.</b> That number is the whole live conversation —
/// system prompt, tool schemas, search results, and every prior turn's assistant text — and on
/// a twenty-turn survey it is dominated by accumulation rather than by reading. SC-011 is
/// worded about "the total page content they read", and a run that narrows correctly can still
/// carry a large conversation; measuring the conversation would fail it for the wrong reason.</para>
///
/// <para><b>Reads nested in a <c>batch</c> count individually</b>, because they are individual
/// reads — the batch saves turns, not context (ADR-030 R4).</para>
///
/// <para><b>Content is resolved against the pristine fixture</b>, not the mutated sandbox: a
/// recording stores request fingerprints and the model's responses, never the tool results it
/// was handed back, so the bytes a read returned have to be reconstructed. A run that rewrote a
/// page before re-reading it will have the pre-write bytes counted here; the difference is a
/// frontmatter line or two and cannot move a token total meaningfully. The same reconstruction
/// backs the before-numbers in <c>specs/026-guarded-tool-surface/baseline.md</c>, so the two
/// halves of that comparison are measured identically.</para>
///
/// <para>Tokens are approximated as characters/4, applied identically everywhere this is used.
/// The quantity of interest is a ratio against a budget, not an exact provider token count.</para>
/// </summary>
public static class ReadShapeAccounting
{
    public static ReadAccounting Measure(string recordingPath, string fixtureWikiRoot)
    {
        var sample = RecordingSerialization.Load(recordingPath);
        var contentTokens = 0;
        var full = 0;
        var ranged = 0;
        var frontmatter = 0;

        foreach (var read in sample.Turns.SelectMany(t => t.ToolUses).SelectMany(EnumerateReads))
        {
            var content = ResolveContent(fixtureWikiRoot, read);
            contentTokens += (content.Length + 3) / 4;

            if (read.FrontmatterOnly)
            {
                frontmatter++;
            }
            else if (read.Offset is not null || read.Limit is not null)
            {
                ranged++;
            }
            else
            {
                full++;
            }
        }

        return new ReadAccounting(contentTokens, full, ranged, frontmatter);
    }

    private sealed record ReadRequest(string Path, int? Offset, int? Limit, bool FrontmatterOnly);

    private static IEnumerable<ReadRequest> EnumerateReads(RecordedToolUse toolUse)
    {
        if (string.Equals(toolUse.ToolName, Grimoire.AgentRuntime.Guardrails.ToolRegistry.ReadFile, StringComparison.Ordinal))
        {
            var parsed = ParseRead(toolUse.InputJson);
            if (parsed is not null)
            {
                yield return parsed;
            }

            yield break;
        }

        if (!string.Equals(toolUse.ToolName, Grimoire.AgentRuntime.Guardrails.ToolRegistry.Batch, StringComparison.Ordinal))
        {
            yield break;
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(toolUse.InputJson).RootElement;
        }
        catch (JsonException)
        {
            yield break;
        }

        if (!root.TryGetProperty("calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var call in calls.EnumerateArray())
        {
            if (!call.TryGetProperty("tool", out var tool)
                || !string.Equals(tool.GetString(), Grimoire.AgentRuntime.Guardrails.ToolRegistry.ReadFile, StringComparison.Ordinal)
                || !call.TryGetProperty("input", out var input))
            {
                continue;
            }

            var parsed = ParseRead(input.GetRawText());
            if (parsed is not null)
            {
                yield return parsed;
            }
        }
    }

    private static ReadRequest? ParseRead(string inputJson)
    {
        try
        {
            var root = JsonDocument.Parse(inputJson).RootElement;
            if (!root.TryGetProperty("path", out var path) || path.GetString() is not { } relative)
            {
                return null;
            }

            return new ReadRequest(
                relative,
                root.TryGetProperty("offset", out var o) && o.TryGetInt32(out var offset) ? offset : null,
                root.TryGetProperty("limit", out var l) && l.TryGetInt32(out var limit) ? limit : null,
                root.TryGetProperty("frontmatter_only", out var f) && f.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResolveContent(string fixtureWikiRoot, ReadRequest read)
    {
        var path = Path.Combine(fixtureWikiRoot, read.Path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            // A read of a page this run created, or a denied read: no fixture bytes to
            // attribute. Counting nothing is the honest default — inventing a size would
            // make the budget comparison depend on a guess.
            return string.Empty;
        }

        var text = File.ReadAllText(path);
        if (read.FrontmatterOnly)
        {
            return ExtractFrontmatter(text);
        }

        if (read.Offset is null && read.Limit is null)
        {
            return text;
        }

        var lines = text.Split('\n');
        var start = Math.Max(0, (read.Offset ?? 1) - 1);
        if (start >= lines.Length)
        {
            return string.Empty;
        }

        var count = read.Limit is { } limit ? Math.Min(limit, lines.Length - start) : lines.Length - start;
        return string.Join('\n', lines.Skip(start).Take(count));
    }

    private static string ExtractFrontmatter(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return string.Empty;
        }

        var block = new List<string> { lines[0] };
        foreach (var line in lines.Skip(1))
        {
            block.Add(line);
            if (line.Trim() == "---")
            {
                break;
            }
        }

        return string.Join('\n', block);
    }
}
