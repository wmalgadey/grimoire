using System.Collections.Concurrent;
using System.Diagnostics;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Grimoire.Ingest.Cache;
using Grimoire.Ingest.Hub;
using Grimoire.Ingest.Models;
using Grimoire.Ingest.Pipeline;
using Grimoire.Ingest.Services;

namespace Grimoire.Ingest.Conversation;

public class ConversationService
{
    private readonly ConcurrentDictionary<string, IngestConversation> _activeConversations = new();

    private readonly IngestCacheRepository _repository;
    private readonly HubReporter _hubReporter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConversationService> _logger;
    private readonly IngestMetrics _metrics;
    private readonly string _model;

    public ConversationService(
        IngestCacheRepository repository,
        HubReporter hubReporter,
        IConfiguration configuration,
        ILogger<ConversationService> logger,
        IngestMetrics metrics)
    {
        _repository = repository;
        _hubReporter = hubReporter;
        _configuration = configuration;
        _logger = logger;
        _metrics = metrics;
        _model = _configuration["Anthropic:Model"] ?? "claude-opus-4-5-20251001";
    }

    public async Task<IngestConversation> OpenConversationAsync(
        string filePath,
        string runId,
        string documentContent,
        List<ChunkAnalysis> analyses)
    {
        var conversation = new IngestConversation
        {
            FilePath = filePath,
            RunId = runId,
            DocumentContent = documentContent,
            ChunkAnalyses = analyses
        };

        var openingMessage = await GenerateOpeningMessageAsync(filePath, documentContent, analyses);
        conversation.OpeningMessage = openingMessage;

        var openingTurn = new ConversationTurn
        {
            ConversationId = conversation.ConversationId,
            TurnIndex = 0,
            Role = TurnRole.Agent,
            Message = openingMessage
        };

        conversation.Turns.Add(openingTurn);

        await _repository.SaveConversationTurnAsync(
            conversation.ConversationId,
            openingTurn.TurnIndex,
            openingTurn.Role.ToString(),
            openingTurn.Message,
            openingTurn.CreatedAt);

        _activeConversations[conversation.ConversationId] = conversation;

        await _hubReporter.PostConversationOpenedAsync(new ConversationOpenedPayload(
            ConversationId: conversation.ConversationId,
            RunId: runId,
            FilePath: filePath,
            OpeningMessage: openingMessage,
            CreatedAt: conversation.CreatedAt));

        _logger.LogInformation(
            "ingest.conversation_turn conversation_id={ConversationId} turn_index=0 role=Agent",
            conversation.ConversationId);

        return conversation;
    }

    public async Task<ConversationTurn> AddUserTurnAsync(string conversationId, string message)
    {
        if (!_activeConversations.TryGetValue(conversationId, out var conversation))
            throw new KeyNotFoundException($"Conversation {conversationId} not found");

        if (conversation.DismissedAt.HasValue)
            throw new InvalidOperationException($"Conversation {conversationId} has been dismissed");

        var userTurnIndex = conversation.Turns.Count;
        var userTurn = new ConversationTurn
        {
            ConversationId = conversationId,
            TurnIndex = userTurnIndex,
            Role = TurnRole.User,
            Message = message
        };
        conversation.Turns.Add(userTurn);

        await _repository.SaveConversationTurnAsync(
            conversationId,
            userTurnIndex,
            userTurn.Role.ToString(),
            message,
            userTurn.CreatedAt);

        // Check for correction patterns
        await CheckForCorrectionsAsync(conversation, message);

        // Generate agent response
        using var activity = IngestTracing.Source.StartActivity("ingest.conversation.turn");
        activity?.SetTag("conversation_id", conversationId);
        activity?.SetTag("turn_index", userTurnIndex);

        var agentMessage = await GenerateAgentResponseAsync(conversation, message);

        var agentTurnIndex = conversation.Turns.Count;
        var agentTurn = new ConversationTurn
        {
            ConversationId = conversationId,
            TurnIndex = agentTurnIndex,
            Role = TurnRole.Agent,
            Message = agentMessage
        };
        conversation.Turns.Add(agentTurn);

        await _repository.SaveConversationTurnAsync(
            conversationId,
            agentTurnIndex,
            agentTurn.Role.ToString(),
            agentMessage,
            agentTurn.CreatedAt);

        _metrics.RecordConversationTurn();
        _logger.LogInformation(
            "ingest.conversation_turn conversation_id={ConversationId} turn_index={TurnIndex} role=Agent",
            conversationId, agentTurnIndex);

        return agentTurn;
    }

    public async Task DismissConversationAsync(string conversationId)
    {
        if (_activeConversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.DismissedAt = DateTimeOffset.UtcNow;
        }
        await Task.CompletedTask;
    }

    public IngestConversation? GetConversation(string conversationId)
    {
        _activeConversations.TryGetValue(conversationId, out var conversation);
        return conversation;
    }

    private async Task<string> GenerateOpeningMessageAsync(
        string filePath,
        string documentContent,
        List<ChunkAnalysis> analyses)
    {
        var topTopics = analyses
            .SelectMany(a => a.Topics)
            .Distinct()
            .Take(10)
            .ToList();

        var prompt = $"""
            You have just processed this document. Provide a 2-3 sentence summary and list the key topics you identified. End with an invitation for questions.

            Document: {filePath}
            Content: {documentContent[..Math.Min(2000, documentContent.Length)]}
            Extracted topics: {string.Join(", ", topTopics)}
            """;

        return await CallLlmAsync(prompt);
    }

    private async Task<string> GenerateAgentResponseAsync(IngestConversation conversation, string userMessage)
    {
        using var activity = IngestTracing.Source.StartActivity("ingest.llm.respond");

        var conversationHistory = string.Join("\n", conversation.Turns
            .Select(t => $"{t.Role}: {t.Message}"));

        var content = conversation.DocumentContent;
        var contentPreview = content[..Math.Min(4000, content.Length)];

        var prompt = $"""
            You are discussing a processed document with the user. Answer questions grounded in the document content.

            Document: {conversation.FilePath}
            Document content (first 4000 chars): {contentPreview}

            Conversation so far:
            {conversationHistory}

            User: {userMessage}
            Respond as Agent:
            """;

        activity?.SetTag("model", _model);

        return await CallLlmAsync(prompt);
    }

    private async Task<string> CallLlmAsync(string prompt)
    {
        var apiKey = GetRequiredApiKey();
        var client = new AnthropicClient(apiKey);

        var response = await client.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model = _model,
            MaxTokens = 1024,
            Messages = [new Message { Role = RoleType.User, Content = prompt }]
        });

        return response.Content.First().ToString() ?? "";
    }

    private string GetRequiredApiKey()
    {
        var apiKey = _configuration["Anthropic:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey;
        }

        _logger.LogError("ingest.config_missing setting=Anthropic:ApiKey");
        throw new InvalidOperationException(
            "Missing required configuration 'Anthropic:ApiKey'. Set Anthropic__ApiKey or Anthropic:ApiKey before running ingest conversation calls.");
    }

    private async Task CheckForCorrectionsAsync(IngestConversation conversation, string message)
    {
        // Check for tag correction patterns: "tag: X" or "add tag X"
        string? tag = null;

        if (message.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            tag = message["tag:".Length..].Trim();
        }
        else if (message.StartsWith("add tag ", StringComparison.OrdinalIgnoreCase))
        {
            tag = message["add tag ".Length..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var record = await _repository.GetRecordAsync(conversation.FilePath);
            if (record != null)
            {
                var corrections = record.UserCorrections ?? "{}";
                // Append the tag correction as JSON
                record.UserCorrections = $"{{\"tag\":\"{tag}\"}}";
                await _repository.SaveRecordAsync(record);

                _logger.LogInformation(
                    "ingest.conversation_turn conversation_id={ConversationId} correction=tag tag={Tag}",
                    conversation.ConversationId, tag);
            }
        }
    }
}
