namespace AgentHub.Api;

public class TaskEntity
{
    public Guid Id { get; set; }
    public Guid? FromAgentId { get; set; }
    public Guid TargetAgentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public decimal? Budget { get; set; }
    public TaskStatus Status { get; set; }
    public string? Result { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
