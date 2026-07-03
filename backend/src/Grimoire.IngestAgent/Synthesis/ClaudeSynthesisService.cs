using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var inferredTitle = InferTitle(sourceContent);
            return new SynthesisResult(
                inferredTitle,
                "Generated without live API key; placeholder summary.",
                "General",
                $"# {inferredTitle}\n\n{sourceContent}\n");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var prompt = "Summarize this source into markdown with a title, one-line summary, and category in JSON keys title, summary, category, content. Source:\n" + sourceContent;
        var body = new
        {
            model = "claude-sonnet-4-5",
            max_tokens = 1200,
            messages = new[] { new { role = "user", content = prompt } },
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var inferred = InferTitle(sourceContent);

        return new SynthesisResult(
            inferred,
            "LLM synthesis completed.",
            "General",
            $"# {inferred}\n\n{sourceContent}\n\n<!-- Raw response: {responseJson[..Math.Min(500, responseJson.Length)]} -->");
    }

    private static string InferTitle(string sourceContent)
    {
        var firstLine = sourceContent.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault(l => l.Length > 0) ?? "Untitled";
        return firstLine.TrimStart('#', ' ').Trim();
    }
}
