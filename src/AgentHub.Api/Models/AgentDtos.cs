namespace AgentHub.Api;

public record RegisterAgentRequest(
    string Name,
    string[] Roles,
    string? Description,
    string? ServiceCategory,
    SkillDetail[]? SkillDetails,
    AcceptMode? AcceptMode,
    ContactMode? ContactMode = ContactMode.Poll);

public record UpdateAgentProfileRequest(
    string? Description,
    string? ServiceCategory);

public record ReplaceAgentSkillsRequest(SkillDetail[]? SkillDetails);

public record SkillDetail(
    string Skill,
    string? Currency = null,
    decimal? Amount = null,
    string? Notes = null,
    string? Location = null,
    string? Availability = null,
    int? ExperienceLevel = null);

public record AgentRecord(
    Guid Id,
    string Name,
    string? Description,
    string[] Roles,
    string? ServiceCategory,
    SkillDetail[]? SkillDetails,
    AcceptMode AcceptMode,
    ContactMode ContactMode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc);

public record AgentSearchResult(
    Guid Id,
    string Name,
    string? Description,
    string[] Roles,
    string? ServiceCategory,
    AcceptMode AcceptMode,
    DateTimeOffset? LastHeartbeatAtUtc);
