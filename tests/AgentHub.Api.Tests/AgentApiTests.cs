using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AgentHub.Api.Tests;

public class AgentApiTests : IClassFixture<AgentHubApiFactory>
{
    private readonly HttpClient _client;

    public AgentApiTests(AgentHubApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RegisterAgent_WithoutName_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "",
            roles = new[] { "provider" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterSeeker_MinimalProfile_ReturnsSuccess()
    {
        var response = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Finder Bot",
            roles = new[] { "seeker" },
            isSearchOnly = true
        });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(body);
        Assert.True(body!.IsSearchOnly);
        Assert.Contains("seeker", body.Roles);
    }

    [Fact]
    public async Task SearchAgents_BySkill_ReturnsMatchingProvider()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Miami Movers Bot",
            roles = new[] { "provider" },
            serviceCategory = "moving",
            location = "Miami",
            skills = new[] { "moving", "loaders", "same-day jobs" }
        });

        register.EnsureSuccessStatusCode();

        var searchResponse = await _client.GetAsync("/api/agents/search?skill=loaders&location=Miami");
        searchResponse.EnsureSuccessStatusCode();

        var results = await searchResponse.Content.ReadFromJsonAsync<List<SearchResponse>>();
        Assert.NotNull(results);
        Assert.Contains(results!, x => x.Name == "Miami Movers Bot");
    }

    [Fact]
    public async Task CreateTask_AndFetchNextTask_Works()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Worker Bot",
            roles = new[] { "provider" }
        });

        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        var taskCreate = await _client.PostAsJsonAsync("/api/tasks", new
        {
            targetAgentId = agent!.Id,
            title = "Need help with a moving job",
            message = "Tomorrow 10am in Miami"
        });

        taskCreate.EnsureSuccessStatusCode();

        var nextTask = await _client.GetAsync($"/api/agents/{agent.Id}/tasks/next");
        nextTask.EnsureSuccessStatusCode();

        var taskBody = await nextTask.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(taskBody);
        Assert.Equal("Need help with a moving job", taskBody!.Title);
        Assert.Equal(agent.Id, taskBody.TargetAgentId);
    }

    [Fact]
    public async Task SubmitTaskResult_UpdatesTaskStatus()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Result Bot",
            roles = new[] { "provider" }
        });

        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        var taskCreate = await _client.PostAsJsonAsync("/api/tasks", new
        {
            targetAgentId = agent!.Id,
            title = "Check schedule"
        });

        taskCreate.EnsureSuccessStatusCode();
        var createdTask = await taskCreate.Content.ReadFromJsonAsync<CreateTaskResponse>();
        Assert.NotNull(createdTask);

        var resultResponse = await _client.PostAsJsonAsync($"/api/tasks/{createdTask!.Id}/result", new
        {
            success = true,
            result = "Available tomorrow at 14:00"
        });

        resultResponse.EnsureSuccessStatusCode();

        var updatedTask = await resultResponse.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(updatedTask);
        Assert.Equal("Completed", updatedTask!.Status);
        Assert.Equal("Available tomorrow at 14:00", updatedTask.Result);
    }

    [Fact]
    public async Task MultipleAgents_CanSearchCreateConversation_AndExchangeMessages()
    {
        var seekerResponse = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Finder Bot",
            roles = new[] { "seeker" },
            isSearchOnly = true,
            skills = new[] { "vendor search" }
        });
        seekerResponse.EnsureSuccessStatusCode();
        var seeker = await seekerResponse.Content.ReadFromJsonAsync<RegisterResponse>();

        var providerResponse = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Miami Movers Bot",
            roles = new[] { "provider" },
            location = "Miami",
            skills = new[] { "moving", "loaders" }
        });
        providerResponse.EnsureSuccessStatusCode();
        var provider = await providerResponse.Content.ReadFromJsonAsync<RegisterResponse>();

        Assert.NotNull(seeker);
        Assert.NotNull(provider);

        var searchResponse = await _client.GetAsync("/api/agents/search?skill=loaders&location=Miami");
        searchResponse.EnsureSuccessStatusCode();
        var matches = await searchResponse.Content.ReadFromJsonAsync<List<SearchResponse>>();
        Assert.NotNull(matches);
        Assert.Contains(matches!, x => x.Id == provider!.Id);

        var conversationResponse = await _client.PostAsJsonAsync("/api/conversations", new
        {
            participantAgentIds = new[] { seeker!.Id, provider!.Id },
            subject = "Need movers in Miami"
        });
        conversationResponse.EnsureSuccessStatusCode();
        var conversation = await conversationResponse.Content.ReadFromJsonAsync<ConversationResponse>();
        Assert.NotNull(conversation);

        var firstMessageResponse = await _client.PostAsJsonAsync($"/api/conversations/{conversation!.Id}/messages", new
        {
            fromAgentId = seeker.Id,
            body = "Need movers tomorrow morning in Miami. Are you available?"
        });
        firstMessageResponse.EnsureSuccessStatusCode();

        var secondMessageResponse = await _client.PostAsJsonAsync($"/api/conversations/{conversation.Id}/messages", new
        {
            fromAgentId = provider.Id,
            body = "Yes, available after 10:00."
        });
        secondMessageResponse.EnsureSuccessStatusCode();

        var inboxResponse = await _client.GetAsync($"/api/agents/{provider.Id}/inbox");
        inboxResponse.EnsureSuccessStatusCode();
        var inbox = await inboxResponse.Content.ReadFromJsonAsync<List<ConversationResponse>>();

        Assert.NotNull(inbox);
        var providerConversation = Assert.Single(inbox!);
        Assert.Equal(2, providerConversation.Messages.Length);
        Assert.Equal("Need movers tomorrow morning in Miami. Are you available?", providerConversation.Messages[0].Body);
        Assert.Equal("Yes, available after 10:00.", providerConversation.Messages[1].Body);
    }

    public sealed record RegisterResponse(Guid Id, string Name, string[] Roles, bool IsSearchOnly);
    public sealed record SearchResponse(Guid Id, string Name, string[] Roles, bool IsSearchOnly);
    public sealed record CreateTaskResponse(Guid Id, string Status, Guid TargetAgentId);
    public sealed record TaskResponse(Guid Id, Guid TargetAgentId, string Title, string Status, string? Result);
    public sealed record ConversationResponse(Guid Id, string? Subject, Guid[] ParticipantAgentIds, MessageResponse[] Messages);
    public sealed record MessageResponse(Guid Id, Guid ConversationId, Guid FromAgentId, string Body);
}

public class AgentHubApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var existing = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AgentHubDbContext>));
            if (existing is not null)
                services.Remove(existing);

            services.AddDbContext<AgentHubDbContext>(options =>
                options.UseInMemoryDatabase("agenthub-tests"));
        });
    }
}
