namespace AgentHub.Api;

public class ConversationEntity
{
    public Guid Id { get; set; }
    public string? Subject { get; set; }
    public string ParticipantAgentIds { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
