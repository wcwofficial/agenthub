using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var connectionString = builder.Configuration.GetConnectionString("AgentHub");

builder.Services.AddDbContext<AgentHubDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseInMemoryDatabase("agenthub-dev");
    }
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AgentHubDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new
{
    name = "AgentHub API",
    version = "0.3.0",
    persistence = string.IsNullOrWhiteSpace(connectionString) ? "InMemory" : "PostgreSQL",
    docs = "/swagger",
    features = new[]
    {
        "Agent registration",
        "Searchable provider directory",
        "Task creation",
        "Agent polling inbox",
        "Task result submission",
        "Conversation threads"
    }
})).WithOpenApi();

app.MapGet("/health", () => Results.Ok(new { ok = true })).WithOpenApi();

app.MapPost("/api/agents/register", async (RegisterAgentRequest request, AgentHubDbContext db) =>
{
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
        Roles = JoinList(request.Roles),
        ServiceCategory = request.ServiceCategory?.Trim(),
        Location = request.Location?.Trim(),
        Skills = JoinList(request.Skills),
        Languages = JoinList(request.Languages),
        Availability = request.Availability?.Trim(),
        PricingCurrency = request.Pricing?.Currency,
        PricingAmount = request.Pricing?.Amount,
        PricingNotes = request.Pricing?.Notes,
        AcceptMode = request.AcceptMode ?? AcceptMode.AskOwnerFirst,
        IsSearchOnly = request.IsSearchOnly,
        ContactMode = request.ContactMode ?? ContactMode.Poll,
        CreatedAtUtc = now,
        LastHeartbeatAtUtc = null
    };

    db.Agents.Add(agent);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        agent.Id,
        agent.Name,
        Roles = SplitList(agent.Roles),
        agent.IsSearchOnly,
        message = "Agent registered successfully"
    });
}).WithOpenApi();

app.MapGet("/api/agents/{id:guid}", async (Guid id, AgentHubDbContext db) =>
{
    var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    return agent is null
        ? Results.NotFound(new { error = "agent not found" })
        : Results.Ok(ToAgentRecord(agent));
}).WithOpenApi();

app.MapGet("/api/agents/search", async (
    string? q,
    string? role,
    string? skill,
    string? location,
    bool? searchOnly,
    AgentHubDbContext db) =>
{
    var query = db.Agents.AsNoTracking().AsQueryable();

    if (!string.IsNullOrWhiteSpace(role))
    {
        var roleNeedle = role.Trim().ToLowerInvariant();
        query = query.Where(a => a.Roles.Contains(roleNeedle));
    }

    if (searchOnly.HasValue)
        query = query.Where(a => a.IsSearchOnly == searchOnly.Value);

    var agents = await query.OrderBy(a => a.Name).ToListAsync();

    if (!string.IsNullOrWhiteSpace(q))
    {
        var needle = q.Trim();
        agents = agents.Where(a =>
            a.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            (a.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
            SplitList(a.Skills).Any(s => s.Contains(needle, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    if (!string.IsNullOrWhiteSpace(skill))
    {
        var skillNeedle = skill.Trim();
        agents = agents.Where(a => SplitList(a.Skills).Any(s => s.Contains(skillNeedle, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    if (!string.IsNullOrWhiteSpace(location))
    {
        var locationNeedle = location.Trim();
        agents = agents.Where(a => a.Location?.Contains(locationNeedle, StringComparison.OrdinalIgnoreCase) ?? false).ToList();
    }

    var results = agents.Select(a => new AgentSearchResult(
        a.Id,
        a.Name,
        a.Description,
        SplitList(a.Roles),
        a.ServiceCategory,
        a.Location,
        SplitList(a.Skills),
        a.Availability,
        ToPricingInfo(a),
        a.AcceptMode,
        a.IsSearchOnly,
        a.LastHeartbeatAtUtc)).ToArray();

    return Results.Ok(results);
}).WithOpenApi();

app.MapPatch("/api/agents/{id:guid}/profile", async (Guid id, UpdateAgentProfileRequest request, AgentHubDbContext db) =>
{
    var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == id);
    if (agent is null)
        return Results.NotFound(new { error = "agent not found" });

    agent.Description = request.Description ?? agent.Description;
    agent.ServiceCategory = request.ServiceCategory ?? agent.ServiceCategory;
    agent.Location = request.Location ?? agent.Location;
    agent.Skills = request.Skills is null ? agent.Skills : JoinList(request.Skills);
    agent.Languages = request.Languages is null ? agent.Languages : JoinList(request.Languages);
    agent.Availability = request.Availability ?? agent.Availability;
    agent.PricingCurrency = request.Pricing?.Currency ?? agent.PricingCurrency;
    agent.PricingAmount = request.Pricing?.Amount ?? agent.PricingAmount;
    agent.PricingNotes = request.Pricing?.Notes ?? agent.PricingNotes;
    agent.IsSearchOnly = request.IsSearchOnly ?? agent.IsSearchOnly;

    await db.SaveChangesAsync();
    return Results.Ok(ToAgentRecord(agent));
}).WithOpenApi();

app.MapPost("/api/agents/{id:guid}/heartbeat", async (Guid id, AgentHubDbContext db) =>
{
    var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == id);
    if (agent is null)
        return Results.NotFound(new { error = "agent not found" });

    agent.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    var pendingCount = await db.Tasks.CountAsync(t => t.TargetAgentId == id && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.Claimed));

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

    if (string.IsNullOrWhiteSpace(request.Title))
        return Results.BadRequest(new { error = "title is required" });

    var task = new TaskEntity
    {
        Id = Guid.NewGuid(),
        FromAgentId = request.FromAgentId,
        TargetAgentId = request.TargetAgentId.Value,
        Title = request.Title.Trim(),
        Message = request.Message?.Trim(),
        Budget = request.Budget,
        Status = TaskStatus.Pending,
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

app.MapGet("/api/agents/{id:guid}/tasks/next", async (Guid id, AgentHubDbContext db) =>
{
    var agentExists = await db.Agents.AsNoTracking().AnyAsync(a => a.Id == id);
    if (!agentExists)
        return Results.NotFound(new { error = "agent not found" });

    var nextTask = await db.Tasks.AsNoTracking()
        .Where(t => t.TargetAgentId == id && t.Status == TaskStatus.Pending)
        .OrderBy(t => t.CreatedAtUtc)
        .FirstOrDefaultAsync();

    return nextTask is null
        ? Results.NoContent()
        : Results.Ok(ToTaskRecord(nextTask));
}).WithOpenApi();

app.MapPost("/api/tasks/{id:guid}/claim", async (Guid id, AgentHubDbContext db) =>
{
    var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
    if (task is null)
        return Results.NotFound(new { error = "task not found" });

    if (task.Status != TaskStatus.Pending)
        return Results.BadRequest(new { error = "task is not pending" });

    task.Status = TaskStatus.Claimed;
    task.ClaimedAtUtc = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(ToTaskRecord(task));
}).WithOpenApi();

app.MapPost("/api/tasks/{id:guid}/result", async (Guid id, SubmitTaskResultRequest request, AgentHubDbContext db) =>
{
    var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
    if (task is null)
        return Results.NotFound(new { error = "task not found" });

    task.Status = request.Success ? TaskStatus.Completed : TaskStatus.Failed;
    task.Result = request.Result?.Trim();
    task.CompletedAtUtc = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(ToTaskRecord(task));
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
        ParticipantAgentIds = JoinGuidList(participantIds),
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    db.Conversations.Add(conversation);
    await db.SaveChangesAsync();

    return Results.Ok(ToConversationRecord(conversation, []));
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

    return Results.Ok(ToConversationRecord(conversation, messages));
}).WithOpenApi();

app.MapPost("/api/conversations/{id:guid}/messages", async (Guid id, CreateMessageRequest request, AgentHubDbContext db) =>
{
    var conversation = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id);
    if (conversation is null)
        return Results.NotFound(new { error = "conversation not found" });

    var participantIds = SplitGuidList(conversation.ParticipantAgentIds);
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

    return Results.Ok(ToMessageRecord(message));
}).WithOpenApi();

app.MapGet("/api/agents/{id:guid}/inbox", async (Guid id, AgentHubDbContext db) =>
{
    var agentExists = await db.Agents.AsNoTracking().AnyAsync(a => a.Id == id);
    if (!agentExists)
        return Results.NotFound(new { error = "agent not found" });

    var conversations = await db.Conversations.AsNoTracking()
        .Where(c => c.ParticipantAgentIds.Contains(id.ToString()))
        .OrderByDescending(c => c.CreatedAtUtc)
        .ToListAsync();

    var messages = await db.Messages.AsNoTracking()
        .Where(m => conversations.Select(c => c.Id).Contains(m.ConversationId))
        .OrderBy(m => m.CreatedAtUtc)
        .ToListAsync();

    var result = conversations.Select(c => ToConversationRecord(c, messages.Where(m => m.ConversationId == c.Id).ToList())).ToArray();
    return Results.Ok(result);
}).WithOpenApi();

app.Run();

static string[] NormalizeStrings(IEnumerable<string>? values) =>
    values?.Select(v => v.Trim())
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];

static string JoinList(IEnumerable<string>? values) => string.Join('|', NormalizeStrings(values));
static string[] SplitList(string? value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
static string JoinGuidList(IEnumerable<Guid>? values) => string.Join('|', values?.Distinct() ?? []);
static Guid[] SplitGuidList(string? value) => string.IsNullOrWhiteSpace(value) ? [] : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse).ToArray();

static PricingInfo? ToPricingInfo(AgentEntity agent) =>
    agent.PricingCurrency is null && agent.PricingAmount is null && agent.PricingNotes is null
        ? null
        : new PricingInfo(agent.PricingCurrency, agent.PricingAmount, agent.PricingNotes);

static AgentRecord ToAgentRecord(AgentEntity agent) => new(
    agent.Id,
    agent.Name,
    agent.Description,
    SplitList(agent.Roles),
    agent.ServiceCategory,
    agent.Location,
    SplitList(agent.Skills),
    SplitList(agent.Languages),
    agent.Availability,
    ToPricingInfo(agent),
    agent.AcceptMode,
    agent.IsSearchOnly,
    agent.ContactMode,
    agent.CreatedAtUtc,
    agent.LastHeartbeatAtUtc);

static TaskRecord ToTaskRecord(TaskEntity task) => new(
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

static MessageRecord ToMessageRecord(MessageEntity message) => new(
    message.Id,
    message.ConversationId,
    message.FromAgentId,
    message.Body,
    message.CreatedAtUtc);

static ConversationRecord ToConversationRecord(ConversationEntity conversation, IEnumerable<MessageEntity> messages) => new(
    conversation.Id,
    conversation.Subject,
    SplitGuidList(conversation.ParticipantAgentIds),
    conversation.CreatedAtUtc,
    messages.OrderBy(m => m.CreatedAtUtc).Select(ToMessageRecord).ToArray());

public record RegisterAgentRequest(
    string Name,
    string[] Roles,
    string? Description,
    string? ServiceCategory,
    string? Location,
    string[]? Skills,
    string[]? Languages,
    string? Availability,
    PricingInfo? Pricing,
    AcceptMode? AcceptMode,
    bool IsSearchOnly = false,
    ContactMode? ContactMode = ContactMode.Poll);

public record UpdateAgentProfileRequest(
    string? Description,
    string? ServiceCategory,
    string? Location,
    string[]? Skills,
    string[]? Languages,
    string? Availability,
    PricingInfo? Pricing,
    bool? IsSearchOnly);

public record CreateTaskRequest(
    Guid? FromAgentId,
    Guid? TargetAgentId,
    string Title,
    string? Message,
    decimal? Budget);

public record SubmitTaskResultRequest(bool Success, string? Result);
public record CreateConversationRequest(Guid[] ParticipantAgentIds, string? Subject);
public record CreateMessageRequest(Guid FromAgentId, string Body);
public record PricingInfo(string? Currency, decimal? Amount, string? Notes);

public record AgentRecord(
    Guid Id,
    string Name,
    string? Description,
    string[] Roles,
    string? ServiceCategory,
    string? Location,
    string[] Skills,
    string[] Languages,
    string? Availability,
    PricingInfo? Pricing,
    AcceptMode AcceptMode,
    bool IsSearchOnly,
    ContactMode ContactMode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc);

public record AgentSearchResult(
    Guid Id,
    string Name,
    string? Description,
    string[] Roles,
    string? ServiceCategory,
    string? Location,
    string[] Skills,
    string? Availability,
    PricingInfo? Pricing,
    AcceptMode AcceptMode,
    bool IsSearchOnly,
    DateTimeOffset? LastHeartbeatAtUtc);

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

public record ConversationRecord(
    Guid Id,
    string? Subject,
    Guid[] ParticipantAgentIds,
    DateTimeOffset CreatedAtUtc,
    MessageRecord[] Messages);

public record MessageRecord(
    Guid Id,
    Guid ConversationId,
    Guid FromAgentId,
    string Body,
    DateTimeOffset CreatedAtUtc);

public class AgentEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Roles { get; set; } = string.Empty;
    public string? ServiceCategory { get; set; }
    public string? Location { get; set; }
    public string Skills { get; set; } = string.Empty;
    public string Languages { get; set; } = string.Empty;
    public string? Availability { get; set; }
    public string? PricingCurrency { get; set; }
    public decimal? PricingAmount { get; set; }
    public string? PricingNotes { get; set; }
    public AcceptMode AcceptMode { get; set; }
    public bool IsSearchOnly { get; set; }
    public ContactMode ContactMode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
}

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

public class ConversationEntity
{
    public Guid Id { get; set; }
    public string? Subject { get; set; }
    public string ParticipantAgentIds { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class MessageEntity
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid FromAgentId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class AgentHubDbContext(DbContextOptions<AgentHubDbContext> options) : DbContext(options)
{
    public DbSet<AgentEntity> Agents => Set<AgentEntity>();
    public DbSet<TaskEntity> Tasks => Set<TaskEntity>();
    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.Roles).IsRequired();
            entity.Property(x => x.Skills).IsRequired();
            entity.Property(x => x.Languages).IsRequired();
        });

        modelBuilder.Entity<TaskEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).IsRequired();
        });

        modelBuilder.Entity<ConversationEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ParticipantAgentIds).IsRequired();
        });

        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Body).IsRequired();
        });
    }
}

public enum AcceptMode
{
    AutoAccept,
    AskOwnerFirst,
    NeverAuto
}

public enum ContactMode
{
    Poll,
    Webhook,
    ManualOnly
}

public enum TaskStatus
{
    Pending,
    Claimed,
    Completed,
    Failed
}

public partial class Program;
