using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace AgentHub.Api;

public static class AgentHubAuth
{
    public const string RegistrationKeyHeader = "X-AgentHub-Registration-Key";

    public static string GenerateApiKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    /// <summary>Returns <c>401</c> when a registration key is configured but missing or wrong.</summary>
    public static IResult? RequireRegistrationKeyIfConfigured(HttpContext http, AgentHubSecurityOptions? options)
    {
        var expected = options?.RegistrationApiKey?.Trim();
        if (string.IsNullOrEmpty(expected))
            return null;

        if (!http.Request.Headers.TryGetValue(RegistrationKeyHeader, out StringValues providedHeader))
            return Results.Unauthorized();

        var provided = providedHeader.ToString().Trim();
        if (!FixedTimeStringEquals(provided, expected))
            return Results.Unauthorized();

        return null;
    }

    public static bool FixedTimeStringEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

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
