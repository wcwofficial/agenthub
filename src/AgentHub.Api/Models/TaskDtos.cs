namespace AgentHub.Api;

public record CreateTaskRequest(
    Guid? FromAgentId,
    Guid? TargetAgentId,
    string Title,
    string? Message,
    decimal? Budget);

public record SubmitTaskResultRequest(bool Success, string? Result);

public record TaskRecord(
    Guid Id,
    Guid? FromAgentId,
    Guid TargetAgentId,
    string Title,
    string? Message,
    decimal? Budget,
    TaskStatus Status,
    string? Result,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? CompletedAtUtc);
