namespace AgentHub.Api;

public class AgentEntity
{
    public Guid Id { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Roles { get; set; } = string.Empty;
    public string? ServiceCategory { get; set; }
    public string? Location { get; set; }
    public string? PricingCurrency { get; set; }
    public decimal? PricingAmount { get; set; }
    public string? PricingNotes { get; set; }
    public AcceptMode AcceptMode { get; set; }
    public ContactMode ContactMode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    public List<AgentSkillEntity> AgentSkills { get; set; } = new();
}
