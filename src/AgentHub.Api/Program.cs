using System.Text.Json.Serialization;
using AgentHub.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.Configure<AgentHubSecurityOptions>(
    builder.Configuration.GetSection(AgentHubSecurityOptions.SectionName));
builder.Services.AddAgentHubRateLimiting();

var connectionString = builder.Configuration.GetConnectionString("AgentHub");

builder.Services.AddDbContext<AgentHubDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
        options.UseNpgsql(connectionString);
    else
        options.UseInMemoryDatabase("agenthub-dev");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AgentHubDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAgentHubSecurityHeaders();
app.UseHttpsRedirection();
if (!app.Environment.IsEnvironment("Testing"))
    app.UseRateLimiter();

app.MapAgentHubRoutes(connectionString);

app.Run();

public partial class Program;
