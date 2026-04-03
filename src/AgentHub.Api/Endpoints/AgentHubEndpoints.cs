using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentHub.Api;

public static class AgentHubEndpoints
{
    public static void MapAgentHubRoutes(this WebApplication app, string? connectionString)
    {
        app.MapGet("/", () => Results.Ok(new
        {
            name = "AgentHub API",
            version = "0.5.0",
            persistence = string.IsNullOrWhiteSpace(connectionString) ? "InMemory" : "PostgreSQL",
            docs = "/swagger",
            agentOnboarding = "/api/meta/agent-onboarding",
            wellKnown = "/.well-known/agenthub.json",
            features = new[]
            {
                "Agent registration",
                "Searchable provider directory",
                "Task creation",
                "Agent polling inbox",
                "Task result submission",
                "Conversation threads",
                "Agent self-delete (authenticated)",
                "Task acceptance flow (AskOwnerFirst) and cancel/decline",
                "Rate limits (global + registration)",
                "Optional registration API key",
                "Security headers",
                "Public agent onboarding JSON for third-party runtimes"
            }
        })).WithOpenApi();

        app.MapGet("/health", () => Results.Ok(new { ok = true })).WithOpenApi();

        app.MapGet("/api/meta/agent-onboarding", (HttpContext http, IOptions<AgentHubSecurityOptions> options) =>
            Results.Ok(AgentOnboardingResponseBuilder.Build(http.Request, options)))
            .WithOpenApi()
            .WithTags("Meta");

        app.MapGet("/.well-known/agenthub.json", (HttpContext http, IOptions<AgentHubSecurityOptions> options) =>
            Results.Ok(AgentOnboardingResponseBuilder.Build(http.Request, options)))
            .WithOpenApi()
            .WithTags("Meta");

        var registerEndpoint = app.MapPost("/api/agents/register", async (
            RegisterAgentRequest request,
            AgentHubDbContext db,
            HttpContext http,
            IOptions<AgentHubSecurityOptions> security) =>
        {
            var regGate = AgentHubAuth.RequireRegistrationKeyIfConfigured(http, security.Value);
            if (regGate is not null)
                return regGate;

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "name is required" });

            if (request.Roles is null || request.Roles.Length == 0)
                return Results.BadRequest(new { error = "at least one role is required" });

            var now = DateTimeOffset.UtcNow;
            var agent = new AgentEntity
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                Roles = AgentHubFormatHelpers.JoinList(request.Roles),
                ServiceCategory = request.ServiceCategory?.Trim(),
                AcceptMode = request.AcceptMode ?? AcceptMode.AskOwnerFirst,
                ContactMode = request.ContactMode ?? ContactMode.Poll,
                ApiKey = AgentHubAuth.GenerateApiKey(),
                CreatedAtUtc = now,
                LastHeartbeatAtUtc = null
            };

            if (request.SkillDetails != null)
            {
                foreach (var skillDetail in request.SkillDetails)
                {
                    agent.AgentSkills.Add(new AgentSkillEntity
                    {
                        Id = Guid.NewGuid(),
                        AgentId = agent.Id,
                        Skill = skillDetail.Skill.Trim(),
                        Currency = skillDetail.Currency,
                        Amount = skillDetail.Amount,
                        Notes = skillDetail.Notes,
                        Location = skillDetail.Location,
                        Availability = skillDetail.Availability,
                        ExperienceLevel = skillDetail.ExperienceLevel
                    });
                }
            }

            db.Agents.Add(agent);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                agent.Id,
                agent.Name,
                Roles = AgentHubFormatHelpers.SplitList(agent.Roles),
                agent.ApiKey,
                message = "Agent registered successfully"
            });
        });

        if (!app.Environment.IsEnvironment("Testing"))
            registerEndpoint.RequireRateLimiting("register");

        registerEndpoint.WithOpenApi();

        app.MapGet("/api/agents/{id:guid}", async (Guid id, AgentHubDbContext db) =>
        {
            var agent = await db.Agents
                .AsNoTracking()
                .Include(a => a.AgentSkills)
                .FirstOrDefaultAsync(a => a.Id == id);
            return agent is null
                ? Results.NotFound(new { error = "agent not found" })
                : Results.Ok(AgentHubMapper.ToAgentRecord(agent));
        }).WithOpenApi();

        app.MapDelete("/api/agents/{id:guid}", async (HttpContext http, Guid id, AgentHubDbContext db) =>
        {
            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, id);
            if (authResult is not null)
                return authResult;

            var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == id);
            if (agent is null)
                return Results.NotFound(new { error = "agent not found" });

            var participantFilter = "|" + id.ToString() + "|";
            var touchedConversations = await db.Conversations
                .Where(c => ("|" + c.ParticipantAgentIds + "|").Contains(participantFilter))
                .ToListAsync();

            var conversationIdsToRemove = new List<Guid>();
            foreach (var conv in touchedConversations)
            {
                var remaining = AgentHubFormatHelpers.SplitGuidList(conv.ParticipantAgentIds)
                    .Where(g => g != id)
                    .Distinct()
                    .ToArray();
                if (remaining.Length < 2)
                    conversationIdsToRemove.Add(conv.Id);
                else
                    conv.ParticipantAgentIds = AgentHubFormatHelpers.JoinGuidList(remaining);
            }

            if (conversationIdsToRemove.Count > 0)
            {
                var messages = await db.Messages.Where(m => conversationIdsToRemove.Contains(m.ConversationId)).ToListAsync();
                db.Messages.RemoveRange(messages);
                var dropConversations = touchedConversations.Where(c => conversationIdsToRemove.Contains(c.Id)).ToList();
                db.Conversations.RemoveRange(dropConversations);
            }

            var tasks = await db.Tasks.Where(t => t.TargetAgentId == id || t.FromAgentId == id).ToListAsync();
            db.Tasks.RemoveRange(tasks);

            db.Agents.Remove(agent);
            await db.SaveChangesAsync();

            return Results.NoContent();
        }).WithOpenApi();

        app.MapGet("/api/agents/search", async (
            string? q,
            string? role,
            string? skill,
            string? location,
            AgentHubDbContext db) =>
        {
            var query = db.Agents.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(role))
            {
                var roleNeedle = role.Trim().ToLowerInvariant();
                query = query.Where(a => a.Roles.Contains(roleNeedle));
            }

            var agents = await query
                .Include(a => a.AgentSkills)
                .OrderBy(a => a.Name)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                agents = agents.Where(a =>
                    a.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    (a.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (a.AgentSkills.Any(s => s.Skill.Contains(needle, StringComparison.OrdinalIgnoreCase)) ||
                     a.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                     (a.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
            }

            if (!string.IsNullOrWhiteSpace(skill))
            {
                var skillNeedle = skill.Trim();
                agents = agents.Where(a => a.AgentSkills.Any(s => s.Skill.Contains(skillNeedle, StringComparison.OrdinalIgnoreCase))).ToList();
            }

            if (!string.IsNullOrWhiteSpace(location))
            {
                var locationNeedle = location.Trim();
                agents = agents.Where(a => a.AgentSkills.Any(s =>
                    s.Location != null && s.Location.Contains(locationNeedle, StringComparison.OrdinalIgnoreCase))).ToList();
            }

            var results = agents.Select(a => new AgentSearchResult(
                a.Id,
                a.Name,
                a.Description,
                AgentHubFormatHelpers.SplitList(a.Roles),
                a.ServiceCategory,
                a.AcceptMode,
                a.LastHeartbeatAtUtc)).ToArray();

            return Results.Ok(results);
        }).WithOpenApi();

        app.MapPatch("/api/agents/{id:guid}/profile", async (HttpContext http, Guid id, UpdateAgentProfileRequest request, AgentHubDbContext db) =>
        {
            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, id);
            if (authResult is not null)
                return authResult;

            var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == id);
            if (agent is null)
                return Results.NotFound(new { error = "agent not found" });

            var agentChanged = false;

            if (request.Description != null && agent.Description != request.Description)
            {
                agent.Description = request.Description;
                agentChanged = true;
            }
            if (request.ServiceCategory != null && agent.ServiceCategory != request.ServiceCategory)
            {
                agent.ServiceCategory = request.ServiceCategory;
                agentChanged = true;
            }

            if (agentChanged)
                await db.SaveChangesAsync();

            var updatedAgent = await db.Agents
                .Include(a => a.AgentSkills)
                .FirstOrDefaultAsync(a => a.Id == id);

            return Results.Ok(AgentHubMapper.ToAgentRecord(updatedAgent!));
        }).WithOpenApi();

        app.MapPut("/api/agents/{id:guid}/skills", async (HttpContext http, Guid id, ReplaceAgentSkillsRequest request, AgentHubDbContext db) =>
        {
            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, id);
            if (authResult is not null)
                return authResult;

            var agentExists = await db.Agents.AnyAsync(a => a.Id == id);
            if (!agentExists)
                return Results.NotFound(new { error = "agent not found" });

            var skillDetails = request.SkillDetails ?? [];
            var trimmedNames = skillDetails.Select(s => s.Skill?.Trim() ?? "").ToList();
            if (trimmedNames.Any(string.IsNullOrWhiteSpace))
                return Results.BadRequest(new { error = "each skill must have a non-empty name" });
            if (trimmedNames.Count != trimmedNames.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                return Results.BadRequest(new { error = "duplicate skill names are not allowed" });

            var existing = await db.AgentSkills.Where(s => s.AgentId == id).ToListAsync();
            if (existing.Count > 0)
                db.AgentSkills.RemoveRange(existing);

            foreach (var skillDetail in skillDetails)
            {
                db.AgentSkills.Add(new AgentSkillEntity
                {
                    Id = Guid.NewGuid(),
                    AgentId = id,
                    Skill = skillDetail.Skill!.Trim(),
                    Currency = skillDetail.Currency,
                    Amount = skillDetail.Amount,
                    Notes = skillDetail.Notes,
                    Location = skillDetail.Location,
                    Availability = skillDetail.Availability,
                    ExperienceLevel = skillDetail.ExperienceLevel
                });
            }

            await db.SaveChangesAsync();

            var updatedAgent = await db.Agents
                .AsNoTracking()
                .Include(a => a.AgentSkills)
                .FirstAsync(a => a.Id == id);

            return Results.Ok(AgentHubMapper.ToAgentRecord(updatedAgent));
        }).WithOpenApi();

        app.MapPost("/api/agents/{id:guid}/heartbeat", async (HttpContext http, Guid id, AgentHubDbContext db) =>
        {
            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, id);
            if (authResult is not null)
                return authResult;

            var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == id);
            if (agent is null)
                return Results.NotFound(new { error = "agent not found" });

            agent.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            var pendingCount = await db.Tasks.CountAsync(t =>
                t.TargetAgentId == id
                && (t.Status == TaskStatus.AwaitingTargetAcceptance
                    || t.Status == TaskStatus.Pending
                    || t.Status == TaskStatus.Claimed));

            return Results.Ok(new
            {
                ok = true,
                agentId = id,
                pendingTasks = pendingCount,
                lastHeartbeatAtUtc = agent.LastHeartbeatAtUtc
            });
        }).WithOpenApi();

        app.MapPost("/api/tasks", async (CreateTaskRequest request, AgentHubDbContext db) =>
        {
            if (request.TargetAgentId is null)
                return Results.BadRequest(new { error = "targetAgentId is required" });

            var targetAgent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.TargetAgentId.Value);
            if (targetAgent is null)
                return Results.NotFound(new { error = "target agent not found" });

            if (targetAgent.AcceptMode == AcceptMode.NeverAuto)
                return Results.Conflict(new { error = "target agent does not accept tasks automatically" });

            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest(new { error = "title is required" });

            var initialStatus = targetAgent.AcceptMode == AcceptMode.AskOwnerFirst
                ? TaskStatus.AwaitingTargetAcceptance
                : TaskStatus.Pending;

            var task = new TaskEntity
            {
                Id = Guid.NewGuid(),
                FromAgentId = request.FromAgentId,
                TargetAgentId = request.TargetAgentId.Value,
                Title = request.Title.Trim(),
                Message = request.Message?.Trim(),
                Budget = request.Budget,
                Status = initialStatus,
                Result = null,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                task.Id,
                task.Status,
                task.TargetAgentId,
                targetAgent = targetAgent.Name
            });
        }).WithOpenApi();

        app.MapGet("/api/agents/{id:guid}/tasks/next", async (HttpContext http, Guid id, AgentHubDbContext db) =>
        {
            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, id);
            if (authResult is not null)
                return authResult;

            var agentExists = await db.Agents.AsNoTracking().AnyAsync(a => a.Id == id);
            if (!agentExists)
                return Results.NotFound(new { error = "agent not found" });

            var nextTask = await db.Tasks.AsNoTracking()
                .Where(t => t.TargetAgentId == id
                    && (t.Status == TaskStatus.AwaitingTargetAcceptance || t.Status == TaskStatus.Pending))
                .OrderBy(t => t.Status == TaskStatus.AwaitingTargetAcceptance ? 0 : 1)
                .ThenBy(t => t.CreatedAtUtc)
                .FirstOrDefaultAsync();

            return nextTask is null
                ? Results.NoContent()
                : Results.Ok(AgentHubMapper.ToTaskRecord(nextTask));
        }).WithOpenApi();

        app.MapPost("/api/tasks/{id:guid}/claim", async (HttpContext http, Guid id, AgentHubDbContext db) =>
        {
            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task is null)
                return Results.NotFound(new { error = "task not found" });

            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, task.TargetAgentId);
            if (authResult is not null)
                return authResult;

            if (task.Status != TaskStatus.Pending)
                return Results.BadRequest(new { error = "task is not pending" });

            task.Status = TaskStatus.Claimed;
            task.ClaimedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(AgentHubMapper.ToTaskRecord(task));
        }).WithOpenApi();

        app.MapPost("/api/tasks/{id:guid}/accept", async (HttpContext http, Guid id, AgentHubDbContext db) =>
        {
            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task is null)
                return Results.NotFound(new { error = "task not found" });

            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, task.TargetAgentId);
            if (authResult is not null)
                return authResult;

            if (task.Status != TaskStatus.AwaitingTargetAcceptance)
                return Results.BadRequest(new { error = "task is not awaiting acceptance" });

            task.Status = TaskStatus.Pending;
            await db.SaveChangesAsync();

            return Results.Ok(AgentHubMapper.ToTaskRecord(task));
        }).WithOpenApi();

        app.MapPost("/api/tasks/{id:guid}/decline", async (HttpContext http, Guid id, DeclineTaskRequest? request, AgentHubDbContext db) =>
        {
            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task is null)
                return Results.NotFound(new { error = "task not found" });

            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, task.TargetAgentId);
            if (authResult is not null)
                return authResult;

            if (task.Status != TaskStatus.AwaitingTargetAcceptance)
                return Results.BadRequest(new { error = "task is not awaiting acceptance" });

            task.Status = TaskStatus.Declined;
            task.Result = request?.Reason?.Trim();
            task.CompletedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(AgentHubMapper.ToTaskRecord(task));
        }).WithOpenApi();

        app.MapPost("/api/tasks/{id:guid}/cancel", async (HttpContext http, Guid id, CancelTaskRequest? request, AgentHubDbContext db) =>
        {
            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task is null)
                return Results.NotFound(new { error = "task not found" });

            if (task.FromAgentId is Guid seekerId)
            {
                var asSeeker = await AgentHubAuth.RequireAgentAuth(http, db, seekerId);
                if (asSeeker is not null)
                {
                    var asTarget = await AgentHubAuth.RequireAgentAuth(http, db, task.TargetAgentId);
                    if (asTarget is not null)
                        return asTarget;
                }
            }
            else
            {
                var asTargetOnly = await AgentHubAuth.RequireAgentAuth(http, db, task.TargetAgentId);
                if (asTargetOnly is not null)
                    return asTargetOnly;
            }

            if (task.Status is not (TaskStatus.AwaitingTargetAcceptance or TaskStatus.Pending or TaskStatus.Claimed))
                return Results.BadRequest(new { error = "task cannot be cancelled in its current state" });

            task.Status = TaskStatus.Cancelled;
            task.Result = request?.Reason?.Trim();
            task.CompletedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(AgentHubMapper.ToTaskRecord(task));
        }).WithOpenApi();

        app.MapPost("/api/tasks/{id:guid}/result", async (HttpContext http, Guid id, SubmitTaskResultRequest request, AgentHubDbContext db) =>
        {
            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task is null)
                return Results.NotFound(new { error = "task not found" });

            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, task.TargetAgentId);
            if (authResult is not null)
                return authResult;

            if (task.Status != TaskStatus.Claimed)
                return Results.BadRequest(new { error = "task must be claimed before submitting a result" });

            task.Status = request.Success ? TaskStatus.Completed : TaskStatus.Failed;
            task.Result = request.Result?.Trim();
            task.CompletedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(AgentHubMapper.ToTaskRecord(task));
        }).WithOpenApi();

        app.MapPost("/api/conversations", async (CreateConversationRequest request, AgentHubDbContext db) =>
        {
            if (request.ParticipantAgentIds is null || request.ParticipantAgentIds.Length < 2)
                return Results.BadRequest(new { error = "at least two participantAgentIds are required" });

            var participantIds = request.ParticipantAgentIds.Distinct().ToArray();
            var existingAgentCount = await db.Agents.CountAsync(a => participantIds.Contains(a.Id));
            if (existingAgentCount != participantIds.Length)
                return Results.BadRequest(new { error = "one or more participant agents do not exist" });

            var conversation = new ConversationEntity
            {
                Id = Guid.NewGuid(),
                Subject = request.Subject?.Trim(),
                ParticipantAgentIds = AgentHubFormatHelpers.JoinGuidList(participantIds),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();

            return Results.Ok(AgentHubMapper.ToConversationRecord(conversation, []));
        }).WithOpenApi();

        app.MapGet("/api/conversations/{id:guid}", async (Guid id, AgentHubDbContext db) =>
        {
            var conversation = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (conversation is null)
                return Results.NotFound(new { error = "conversation not found" });

            var messages = await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == id)
                .OrderBy(m => m.CreatedAtUtc)
                .ToListAsync();

            return Results.Ok(AgentHubMapper.ToConversationRecord(conversation, messages));
        }).WithOpenApi();

        app.MapPost("/api/conversations/{id:guid}/messages", async (HttpContext http, Guid id, CreateMessageRequest request, AgentHubDbContext db) =>
        {
            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, request.FromAgentId);
            if (authResult is not null)
                return authResult;

            var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id);
            if (conversation is null)
                return Results.NotFound(new { error = "conversation not found" });

            var participantIds = AgentHubFormatHelpers.SplitGuidList(conversation.ParticipantAgentIds);
            if (!participantIds.Contains(request.FromAgentId))
                return Results.BadRequest(new { error = "fromAgentId is not a participant of this conversation" });

            if (string.IsNullOrWhiteSpace(request.Body))
                return Results.BadRequest(new { error = "body is required" });

            var message = new MessageEntity
            {
                Id = Guid.NewGuid(),
                ConversationId = id,
                FromAgentId = request.FromAgentId,
                Body = request.Body.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            db.Messages.Add(message);
            await db.SaveChangesAsync();

            return Results.Ok(AgentHubMapper.ToMessageRecord(message));
        }).WithOpenApi();

        app.MapGet("/api/agents/{id:guid}/inbox", async (HttpContext http, Guid id, AgentHubDbContext db) =>
        {
            var authResult = await AgentHubAuth.RequireAgentAuth(http, db, id);
            if (authResult is not null)
                return authResult;

            var agentExists = await db.Agents.AsNoTracking().AnyAsync(a => a.Id == id);
            if (!agentExists)
                return Results.NotFound(new { error = "agent not found" });

            var participantFilter = "|" + id.ToString() + "|";
            var conversations = await db.Conversations.AsNoTracking()
                .Where(c => ("|" + c.ParticipantAgentIds + "|").Contains(participantFilter))
                .OrderByDescending(c => c.CreatedAtUtc)
                .ToListAsync();

            var messages = await db.Messages.AsNoTracking()
                .Where(m => conversations.Select(c => c.Id).Contains(m.ConversationId))
                .OrderBy(m => m.CreatedAtUtc)
                .ToListAsync();

            var result = conversations.Select(c => AgentHubMapper.ToConversationRecord(c, messages.Where(m => m.ConversationId == c.Id).ToList())).ToArray();
            return Results.Ok(result);
        }).WithOpenApi();
    }
}
