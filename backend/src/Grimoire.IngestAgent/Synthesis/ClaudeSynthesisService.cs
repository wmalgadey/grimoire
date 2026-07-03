using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace Grimoire.IngestAgent.Synthesis;

public sealed class ClaudeSynthesisService
{
    private readonly HttpClient _httpClient;

    public ClaudeSynthesisService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<SynthesisResult> SynthesizeAsync(string sourceContent, CancellationToken cancellationToken)
    {
        var prompt = "Summarize this source into a JSON object with keys title, summary, category, content. " +
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

        return payload is null ||
            string.IsNullOrWhiteSpace(payload.Title) ||
            string.IsNullOrWhiteSpace(payload.Summary) ||
            string.IsNullOrWhiteSpace(payload.Category) ||
            string.IsNullOrWhiteSpace(payload.Content)
            ? throw new InvalidOperationException(
                "Claude SDK synthesis returned incomplete payload.\nPayload: " + payloadJson)
            : new SynthesisResult(
            payload.Title.Trim(),
            payload.Summary.Trim(),
            payload.Category.Trim(),
            payload.Content.Trim());
    }

    private static string ExtractPayloadJson(Message response)
    {
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

    private sealed class SynthesisPayload
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Category { get; set; }
        public string? Content { get; set; }
    }

}
