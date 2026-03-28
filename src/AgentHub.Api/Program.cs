using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var agents = new ConcurrentDictionary<Guid, AgentRecord>();
var tasks = new ConcurrentDictionary<Guid, TaskRecord>();

app.MapGet("/", () => Results.Ok(new
{
    name = "AgentHub API",
    version = "0.1.0",
    docs = "/swagger",
    features = new[]
    {
        "Agent registration",
        "Searchable provider directory",
        "Task creation",
        "Agent polling inbox",
        "Task result submission"
    }
})).WithOpenApi();

app.MapGet("/health", () => Results.Ok(new { ok = true })).WithOpenApi();

app.MapPost("/api/agents/register", (RegisterAgentRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "name is required" });

    if (request.Roles is null || request.Roles.Length == 0)
        return Results.BadRequest(new { error = "at least one role is required" });

    var now = DateTimeOffset.UtcNow;
    var agent = new AgentRecord(
        Id: Guid.NewGuid(),
        Name: request.Name.Trim(),
        Description: request.Description?.Trim(),
        Roles: request.Roles.Select(r => r.Trim().ToLowerInvariant()).Distinct().ToArray(),
        ServiceCategory: request.ServiceCategory?.Trim(),
        Location: request.Location?.Trim(),
        Skills: request.Skills?.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToArray() ?? [],
        Languages: request.Languages?.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToArray() ?? [],
        Availability: request.Availability?.Trim(),
        Pricing: request.Pricing,
        AcceptMode: request.AcceptMode ?? AcceptMode.AskOwnerFirst,
        IsSearchOnly: request.IsSearchOnly,
        ContactMode: request.ContactMode ?? ContactMode.Poll,
        CreatedAtUtc: now,
        LastHeartbeatAtUtc: null
    );

    agents[agent.Id] = agent;

    return Results.Ok(new
    {
        agent.Id,
        agent.Name,
        agent.Roles,
        agent.IsSearchOnly,
        message = "Agent registered successfully"
    });
}).WithOpenApi();

app.MapGet("/api/agents/{id:guid}", (Guid id) =>
{
    return agents.TryGetValue(id, out var agent)
        ? Results.Ok(agent)
        : Results.NotFound(new { error = "agent not found" });
}).WithOpenApi();

app.MapGet("/api/agents/search", (
    string? q,
    string? role,
    string? skill,
    string? location,
    bool? searchOnly) =>
{
    IEnumerable<AgentRecord> query = agents.Values;

    if (!string.IsNullOrWhiteSpace(q))
    {
        var needle = q.Trim();
        query = query.Where(a =>
            a.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            (a.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
            a.Skills.Any(s => s.Contains(needle, StringComparison.OrdinalIgnoreCase)));
    }

    if (!string.IsNullOrWhiteSpace(role))
    {
        var roleNeedle = role.Trim().ToLowerInvariant();
        query = query.Where(a => a.Roles.Contains(roleNeedle));
    }

    if (!string.IsNullOrWhiteSpace(skill))
    {
        var skillNeedle = skill.Trim();
        query = query.Where(a => a.Skills.Any(s => s.Contains(skillNeedle, StringComparison.OrdinalIgnoreCase)));
    }

    if (!string.IsNullOrWhiteSpace(location))
    {
        var locationNeedle = location.Trim();
        query = query.Where(a => (a.Location?.Contains(locationNeedle, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    if (searchOnly.HasValue)
        query = query.Where(a => a.IsSearchOnly == searchOnly.Value);

    var results = query
        .OrderBy(a => a.Name)
        .Select(a => new AgentSearchResult(
            a.Id,
            a.Name,
            a.Description,
            a.Roles,
            a.ServiceCategory,
            a.Location,
            a.Skills,
            a.Availability,
            a.Pricing,
            a.AcceptMode,
            a.IsSearchOnly,
            a.LastHeartbeatAtUtc))
        .ToArray();

    return Results.Ok(results);
}).WithOpenApi();

app.MapPatch("/api/agents/{id:guid}/profile", (Guid id, UpdateAgentProfileRequest request) =>
{
    if (!agents.TryGetValue(id, out var current))
        return Results.NotFound(new { error = "agent not found" });

    var updated = current with
    {
        Description = request.Description ?? current.Description,
        ServiceCategory = request.ServiceCategory ?? current.ServiceCategory,
        Location = request.Location ?? current.Location,
        Skills = request.Skills?.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToArray() ?? current.Skills,
        Languages = request.Languages?.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToArray() ?? current.Languages,
        Availability = request.Availability ?? current.Availability,
        Pricing = request.Pricing ?? current.Pricing,
        IsSearchOnly = request.IsSearchOnly ?? current.IsSearchOnly
    };

    agents[id] = updated;
    return Results.Ok(updated);
}).WithOpenApi();

app.MapPost("/api/agents/{id:guid}/heartbeat", (Guid id) =>
{
    if (!agents.TryGetValue(id, out var current))
        return Results.NotFound(new { error = "agent not found" });

    var updated = current with { LastHeartbeatAtUtc = DateTimeOffset.UtcNow };
    agents[id] = updated;

    var pendingCount = tasks.Values.Count(t => t.TargetAgentId == id && t.Status is TaskStatus.Pending or TaskStatus.Claimed);

    return Results.Ok(new
    {
        ok = true,
        agentId = id,
        pendingTasks = pendingCount,
        lastHeartbeatAtUtc = updated.LastHeartbeatAtUtc
    });
}).WithOpenApi();

app.MapPost("/api/tasks", (CreateTaskRequest request) =>
{
    if (request.TargetAgentId is null)
        return Results.BadRequest(new { error = "targetAgentId is required" });

    if (!agents.TryGetValue(request.TargetAgentId.Value, out var targetAgent))
        return Results.NotFound(new { error = "target agent not found" });

    if (string.IsNullOrWhiteSpace(request.Title))
        return Results.BadRequest(new { error = "title is required" });

    var now = DateTimeOffset.UtcNow;
    var task = new TaskRecord(
        Id: Guid.NewGuid(),
        FromAgentId: request.FromAgentId,
        TargetAgentId: request.TargetAgentId.Value,
        Title: request.Title.Trim(),
        Message: request.Message?.Trim(),
        Budget: request.Budget,
        Status: TaskStatus.Pending,
        Result: null,
        CreatedAtUtc: now,
        ClaimedAtUtc: null,
        CompletedAtUtc: null
    );

    tasks[task.Id] = task;

    return Results.Ok(new
    {
        task.Id,
        task.Status,
        task.TargetAgentId,
        targetAgent = targetAgent.Name
    });
}).WithOpenApi();

app.MapGet("/api/agents/{id:guid}/tasks/next", (Guid id) =>
{
    if (!agents.ContainsKey(id))
        return Results.NotFound(new { error = "agent not found" });

    var nextTask = tasks.Values
        .Where(t => t.TargetAgentId == id && t.Status == TaskStatus.Pending)
        .OrderBy(t => t.CreatedAtUtc)
        .FirstOrDefault();

    return nextTask is null
        ? Results.NoContent()
        : Results.Ok(nextTask);
}).WithOpenApi();

app.MapPost("/api/tasks/{id:guid}/claim", (Guid id) =>
{
    if (!tasks.TryGetValue(id, out var current))
        return Results.NotFound(new { error = "task not found" });

    if (current.Status != TaskStatus.Pending)
        return Results.BadRequest(new { error = "task is not pending" });

    var updated = current with
    {
        Status = TaskStatus.Claimed,
        ClaimedAtUtc = DateTimeOffset.UtcNow
    };

    tasks[id] = updated;
    return Results.Ok(updated);
}).WithOpenApi();

app.MapPost("/api/tasks/{id:guid}/result", (Guid id, SubmitTaskResultRequest request) =>
{
    if (!tasks.TryGetValue(id, out var current))
        return Results.NotFound(new { error = "task not found" });

    var updated = current with
    {
        Status = request.Success ? TaskStatus.Completed : TaskStatus.Failed,
        Result = request.Result?.Trim(),
        CompletedAtUtc = DateTimeOffset.UtcNow
    };

    tasks[id] = updated;
    return Results.Ok(updated);
}).WithOpenApi();

app.Run();

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
