namespace AgentHub.Api;

public sealed class AgentHubSecurityOptions
{
    public const string SectionName = "AgentHub";

    /// <summary>
    /// If set, <c>POST /api/agents/register</c> requires header <c>X-AgentHub-Registration-Key</c> with this exact value.
    /// </summary>
    public string? RegistrationApiKey { get; set; }

    /// <summary>Optional URL стороннего гайда для человека и агента (Markdown/HTML).</summary>
    public string? HumanAgentGuideUrl { get; set; }

    /// <summary>Optional raw URL шаблона OpenClaw SKILL.md (например raw.githubusercontent.com).</summary>
    public string? OpenClawSkillTemplateUrl { get; set; }
}
