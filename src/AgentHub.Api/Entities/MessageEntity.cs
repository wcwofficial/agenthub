namespace AgentHub.Api;

public class MessageEntity
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid FromAgentId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
