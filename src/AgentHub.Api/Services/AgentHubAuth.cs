using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace AgentHub.Api;

public static class AgentHubAuth
{
    public static string GenerateApiKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static async Task<IResult?> RequireAgentAuth(HttpContext http, AgentHubDbContext db, Guid agentId)
    {
        if (!http.Request.Headers.TryGetValue("Authorization", out var authHeader))
            return Results.Unauthorized();

        var header = authHeader.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Results.Unauthorized();

        var token = header[7..].Trim();
        if (string.IsNullOrWhiteSpace(token))
            return Results.Unauthorized();

        var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == agentId);
        if (agent is null)
            return Results.NotFound(new { error = "agent not found" });

        return agent.ApiKey == token ? null : Results.Unauthorized();
    }
}
