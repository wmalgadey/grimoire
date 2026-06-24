namespace Grimoire.Api.Ingest.Models;

public record ConversationTurnRecord(
    string ConversationId,
    int TurnIndex,
    string FilePath,
    string Role,
    string Message,
    string CreatedAt
);
