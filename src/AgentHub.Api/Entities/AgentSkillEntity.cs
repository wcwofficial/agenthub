namespace AgentHub.Api;

public class AgentSkillEntity
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public AgentEntity Agent { get; set; } = null!;
    public string Skill { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public decimal? Amount { get; set; }
    public string? Notes { get; set; }
    public string? Location { get; set; }
    public string? Availability { get; set; }
    public int? ExperienceLevel { get; set; }
}
