using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace Grimoire.IngestAgent.Synthesis;

public sealed class ClaudeSynthesisService
{
    public async Task<SynthesisResult> SynthesizeAsync(string sourceContent, CancellationToken cancellationToken)
    {
        var prompt = "Summarize this source into a JSON object with keys title, summary, category, content. " +
                     "Also include planned_pages as an array of objects with keys: kind (source|entity|concept), title, summary, category, content, inbound_links (string array). " +
                     "The content value must be markdown and must start with a single H1 title. " +
                     "Return only JSON with no code fences or extra text. Source:\n" +
                     sourceContent;

        var parameters = new MessageCreateParams
        {
            Model = Model.ClaudeHaiku4_5,
            MaxTokens = 4096,
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = prompt,
                },
            ],
            Tools = BuildTools(),
        };

        Message? response = null;

        try
        {
            AnthropicClient client = new();

            response = await client.Messages.Create(parameters, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Anthropic.Exceptions.AnthropicUnauthorizedException ex)
        {
            throw new InvalidOperationException(
                "Claude SDK synthesis failed due to unauthorized access. Verify ANTHROPIC_AUTH_TOKEN is valid and has access to the Anthropic API.",
                ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Claude SDK synthesis failed.\nResponse: " + (response?.ToString() ?? "null"),
                ex);
        }

        var payloadJson = ExtractPayloadJson(response);
        SynthesisPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<SynthesisPayload>(payloadJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Claude SDK synthesis returned invalid JSON payload.\nPayload: " + payloadJson,
                ex);
        }

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.Title) ||
            string.IsNullOrWhiteSpace(payload.Summary) ||
            string.IsNullOrWhiteSpace(payload.Category) ||
            string.IsNullOrWhiteSpace(payload.Content))
        {
            throw new InvalidOperationException(
                "Claude SDK synthesis returned incomplete payload.\nPayload: " + payloadJson);
        }

        var plannedPages = payload.PlannedPages is { Count: > 0 }
            ? payload.PlannedPages
                .Where(page =>
                    !string.IsNullOrWhiteSpace(page.Kind) &&
                    !string.IsNullOrWhiteSpace(page.Title) &&
                    !string.IsNullOrWhiteSpace(page.Summary) &&
                    !string.IsNullOrWhiteSpace(page.Category) &&
                    !string.IsNullOrWhiteSpace(page.Content))
                .Select(page => new SynthesizedWikiPage(
                    page.Kind!.Trim(),
                    page.Title!.Trim(),
                    page.Summary!.Trim(),
                    page.Category!.Trim(),
                    page.Content!.Trim(),
                    (page.InboundLinks ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList()))
                .ToList()
            : [];

        if (plannedPages.Count == 0)
        {
            plannedPages.Add(new SynthesizedWikiPage(
                Kind: "source",
                Title: payload.Title.Trim(),
                Summary: payload.Summary.Trim(),
                Category: payload.Category.Trim(),
                Content: payload.Content.Trim(),
                InboundLinks: []));
        }

        return new SynthesisResult(
            payload.Title.Trim(),
            payload.Summary.Trim(),
            payload.Category.Trim(),
            payload.Content.Trim(),
            plannedPages);
    }

    private static string ExtractPayloadJson(Message response)
    {
        foreach (var block in response.Content)
        {
            if (!block.TryPickToolUse(out var toolUseBlock))
            {
                continue;
            }

            if (!string.Equals(toolUseBlock.Name, "emit_synthesis_result", StringComparison.Ordinal))
            {
                continue;
            }

            return JsonSerializer.Serialize(toolUseBlock.Input);
        }

        if (response.Content is null || response.Content.Count == 0)
        {
            throw new InvalidOperationException("Claude SDK synthesis returned no content blocks.");
        }

        var textBuilder = new StringBuilder();

        foreach (var block in response.Content)
        {
            if (!block.TryPickText(out var textBlock))
            {
                continue;
            }

            var textValue = textBlock.Text;
            if (!string.IsNullOrWhiteSpace(textValue))
            {
                textBuilder.AppendLine(textValue);
            }
        }

        var text = textBuilder.ToString().Trim();
        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidOperationException("Claude SDK synthesis response contained no text payload.")
            : StripJsonFence(text);
    }

    private static string StripJsonFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 3)
        {
            return trimmed;
        }

        var firstLine = lines[0].Trim();
        var lastLine = lines[^1].Trim();
        return !firstLine.StartsWith("```", StringComparison.Ordinal) || lastLine != "```" ? trimmed : string.Join('\n', lines[1..^1]).Trim();
    }

    private static IReadOnlyList<ToolUnion> BuildTools()
    {
        return
        [
            new Tool
            {
                Name = "emit_synthesis_result",
                Description = "Emit normalized synthesis output for wiki writing, including planned source/entity/concept pages.",
                Strict = true,
                InputSchema = new InputSchema
                {
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["title"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                        ["summary"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                        ["category"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                        ["content"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                        ["planned_pages"] = JsonSerializer.SerializeToElement(new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    kind = new { type = "string", @enum = new[] { "source", "entity", "concept" } },
                                    title = new { type = "string" },
                                    summary = new { type = "string" },
                                    category = new { type = "string" },
                                    content = new { type = "string" },
                                    inbound_links = new
                                    {
                                        type = "array",
                                        items = new { type = "string" },
                                    },
                                },
                                required = new[] { "kind", "title", "summary", "category", "content", "inbound_links" },
                            },
                        }),
                    },
                    Required = ["title", "summary", "category", "content", "planned_pages"],
                },
            },
        ];
    }

    private sealed class SynthesisPayload
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Category { get; set; }
        public string? Content { get; set; }
        public List<PlannedPagePayload>? PlannedPages { get; set; }
    }

    private sealed class PlannedPagePayload
    {
        public string? Kind { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Category { get; set; }
        public string? Content { get; set; }
        public List<string>? InboundLinks { get; set; }
    }

}
