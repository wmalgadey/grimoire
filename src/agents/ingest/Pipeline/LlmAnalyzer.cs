using System.Diagnostics;
using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Grimoire.Ingest.Services;

namespace Grimoire.Ingest.Pipeline;

public record ChunkAnalysis(
    string Summary,
    List<string> Topics,
    List<Entity> Entities,
    List<string> KeyClaims,
    string ContentType);

public record Entity(string Name, string Type);

public class LlmAnalyzer
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LlmAnalyzer> _logger;
    private readonly IngestMetrics _metrics;
    private readonly string _model;

    public LlmAnalyzer(
        IConfiguration configuration,
        ILogger<LlmAnalyzer> logger,
        IngestMetrics metrics)
    {
        _configuration = configuration;
        _logger = logger;
        _metrics = metrics;
        _model = _configuration["Anthropic:Model"]
            ?? _configuration["ANTHROPIC_MODEL"]
            ?? "claude-opus-4-5-20251001";
    }

    public async Task<ChunkAnalysis> AnalyzeChunkAsync(Chunk chunk, string filePath)
    {
        var prompt = BuildPrompt(chunk, filePath);
        const int maxRetries = 3;
        var apiKey = GetRequiredApiKey();

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var activity = IngestTracing.Source.StartActivity("ingest.pipeline.llm_analyze");
                activity?.SetTag("chunk_index", chunk.Index);
                activity?.SetTag("model", _model);

                var client = new AnthropicClient(apiKey);

                var response = await client.Messages.GetClaudeMessageAsync(new MessageParameters
                {
                    Model = _model,
                    MaxTokens = 2048,
                    Messages = [new Message { Role = RoleType.User, Content = prompt }]
                });

                var text = response.Content.First().ToString() ?? "";

                _logger.LogInformation(
                    "ingest.llm_call file_path={FilePath} chunk_index={ChunkIndex} model={Model}",
                    filePath, chunk.Index, _model);

                _metrics.RecordLlmCall("success");
                activity?.SetTag("input_tokens", response.Usage?.InputTokens ?? 0);

                return ParseAnalysis(text);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "ingest.llm_error file_path={FilePath} chunk_index={ChunkIndex} error={Error} retry_attempt={Attempt}",
                    filePath, chunk.Index, ex.Message, attempt + 1);

                _metrics.RecordLlmCall("failed");

                if (attempt < maxRetries - 1)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 1s, 2s, 4s
                    await Task.Delay(delay);
                }
            }
        }

        // Return minimal analysis on final failure
        return new ChunkAnalysis(
            Summary: "",
            Topics: new List<string>(),
            Entities: new List<Entity>(),
            KeyClaims: new List<string>(),
            ContentType: "other");
    }

    private static string BuildPrompt(Chunk chunk, string filePath)
    {
        var jsonFormat = """
            {
              "summary": "2-3 sentence summary of this chunk",
              "topics": ["topic1", "topic2"],
              "entities": [{"name": "entity name", "type": "person|org|concept|algorithm|location"}],
              "key_claims": ["claim1", "claim2"],
              "content_type": "research-paper|technical-doc|article|other"
            }
            """;

        return $"""
            Analyze this document chunk and extract structured metadata as JSON.

            Document path: {filePath}
            Chunk {chunk.Index + 1} of {chunk.TotalChunks}:

            {chunk.Content}

            Respond with JSON in this exact format:
            {jsonFormat}
            """;
    }

    private static ChunkAnalysis ParseAnalysis(string text)
    {
        try
        {
            // Extract JSON from the response (may be wrapped in markdown)
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start)
                return EmptyAnalysis();

            var json = text[start..(end + 1)];
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
            var contentType = root.TryGetProperty("content_type", out var ct) ? ct.GetString() ?? "other" : "other";

            var topics = root.TryGetProperty("topics", out var t)
                ? t.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => x.Length > 0).ToList()
                : new List<string>();

            var keyClaims = root.TryGetProperty("key_claims", out var kc)
                ? kc.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => x.Length > 0).ToList()
                : new List<string>();

            var entities = new List<Entity>();
            if (root.TryGetProperty("entities", out var ents))
            {
                foreach (var ent in ents.EnumerateArray())
                {
                    var name = ent.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var type = ent.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(name))
                        entities.Add(new Entity(name, type));
                }
            }

            return new ChunkAnalysis(summary, topics, entities, keyClaims, contentType);
        }
        catch
        {
            return EmptyAnalysis();
        }
    }

    private static ChunkAnalysis EmptyAnalysis() =>
        new("", new List<string>(), new List<Entity>(), new List<string>(), "other");

    private string GetRequiredApiKey()
    {
        var apiKey = _configuration["Anthropic:ApiKey"]
            ?? _configuration["ANTHROPIC_API_KEY"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey;
        }

        _logger.LogError("ingest.config_missing setting=Anthropic:ApiKey");
        throw new InvalidOperationException(
            "Missing required configuration 'Anthropic:ApiKey'. Set Anthropic__ApiKey or Anthropic:ApiKey before running ingest analysis.");
    }
}
