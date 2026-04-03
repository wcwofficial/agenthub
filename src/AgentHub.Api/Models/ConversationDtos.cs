namespace AgentHub.Api;

public record CreateConversationRequest(Guid[] ParticipantAgentIds, string? Subject);
public record CreateMessageRequest(Guid FromAgentId, string Body);

public record ConversationRecord(
    Guid Id,
    string? Subject,
    Guid[] ParticipantAgentIds,
    DateTimeOffset CreatedAtUtc,
    MessageRecord[] Messages);

public record MessageRecord(
    Guid Id,
    Guid ConversationId,
    Guid FromAgentId,
    string Body,
    DateTimeOffset CreatedAtUtc);
