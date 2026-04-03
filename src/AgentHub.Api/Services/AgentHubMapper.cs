namespace AgentHub.Api;

public static class AgentHubMapper
{
    public static AgentRecord ToAgentRecord(AgentEntity agent) => new(
        agent.Id,
        agent.Name,
        agent.Description,
        AgentHubFormatHelpers.SplitList(agent.Roles),
        agent.ServiceCategory,
        agent.AgentSkills.Select(s => new SkillDetail(
            s.Skill,
            s.Currency,
            s.Amount,
            s.Notes,
            s.Location,
            s.Availability,
            s.ExperienceLevel)).ToArray(),
        agent.AcceptMode,
        agent.ContactMode,
        agent.CreatedAtUtc,
        agent.LastHeartbeatAtUtc);

    public static TaskRecord ToTaskRecord(TaskEntity task) => new(
        task.Id,
        task.FromAgentId,
        task.TargetAgentId,
        task.Title,
        task.Message,
        task.Budget,
        task.Status,
        task.Result,
        task.CreatedAtUtc,
        task.ClaimedAtUtc,
        task.CompletedAtUtc);

    public static MessageRecord ToMessageRecord(MessageEntity message) => new(
        message.Id,
        message.ConversationId,
        message.FromAgentId,
        message.Body,
        message.CreatedAtUtc);

    public static ConversationRecord ToConversationRecord(ConversationEntity conversation, IEnumerable<MessageEntity> messages) =>
        new(
            conversation.Id,
            conversation.Subject,
            AgentHubFormatHelpers.SplitGuidList(conversation.ParticipantAgentIds),
            conversation.CreatedAtUtc,
            messages.OrderBy(m => m.CreatedAtUtc).Select(ToMessageRecord).ToArray());
}
