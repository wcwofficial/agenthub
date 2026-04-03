using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentHub.Api;

/// <summary>Design-time only: <c>dotnet ef migrations</c> uses Npgsql to scaffold SQL.</summary>
public sealed class AgentHubDbContextFactory : IDesignTimeDbContextFactory<AgentHubDbContext>
{
    public AgentHubDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AgentHubDbContext>();
        var cs = Environment.GetEnvironmentVariable("AGENTHUB_DESIGN_PG")
                 ?? "Host=127.0.0.1;Port=5432;Database=agenthub;Username=agenthub;Password=agenthub";
        optionsBuilder.UseNpgsql(cs);
        return new AgentHubDbContext(optionsBuilder.Options);
    }
}
