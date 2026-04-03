using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
            roles = new[] { "seeker" }
        });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(body);
        Assert.Contains("seeker", body!.Roles);
        Assert.False(string.IsNullOrWhiteSpace(body.ApiKey));
    }

    [Fact]
    public async Task GetAgent_Seeker_HasNoSkillDetails_SearchStillWorks()
    {
        var reg = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Seeker Only",
            roles = new[] { "seeker" }
        });
        reg.EnsureSuccessStatusCode();
        var agent = await reg.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        var get = await _client.GetAsync($"/api/agents/{agent!.Id}");
        get.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(0, root.GetProperty("skillDetails").GetArrayLength());
        Assert.False(root.TryGetProperty("languages", out _));
        Assert.False(root.TryGetProperty("isSearchOnly", out _));
        Assert.False(root.TryGetProperty("availability", out _));
    }

    [Fact]
    public async Task PatchProfile_AndPutSkills_MatchPublicContract()
    {
        var reg = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Contract Bot",
            roles = new[] { "provider" },
            skillDetails = new[] { new { skill = "alpha" } }
        });
        reg.EnsureSuccessStatusCode();
        var agent = await reg.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/agents/{agent!.Id}/profile")
        {
            Content = JsonContent.Create(new { description = "x", serviceCategory = "y" })
        };
        patch.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        (await _client.SendAsync(patch)).EnsureSuccessStatusCode();

        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/agents/{agent.Id}/skills")
        {
            Content = JsonContent.Create(new
            {
                skillDetails = new[] { new { skill = "beta", location = "Berlin", availability = "evenings" } }
            })
        };
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        var putRes = await _client.SendAsync(put);
        putRes.EnsureSuccessStatusCode();
        using var putDoc = JsonDocument.Parse(await putRes.Content.ReadAsStringAsync());
        var sk = putDoc.RootElement.GetProperty("skillDetails")[0];
        Assert.Equal("beta", sk.GetProperty("skill").GetString());
        Assert.Equal("Berlin", sk.GetProperty("location").GetString());
        Assert.Equal("evenings", sk.GetProperty("availability").GetString());
    }

    [Fact]
    public async Task SearchAgents_BySkill_ReturnsMatchingProvider()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Miami Movers Bot",
            roles = new[] { "provider" },
            serviceCategory = "moving",
            skillDetails = new[] {
                new { skill = "moving", location = "Miami" },
                new { skill = "loaders", location = "Miami" },
                new { skill = "same-day jobs", location = "Miami" }
            }
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
            roles = new[] { "provider" },
            acceptMode = "AutoAccept"
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

        using var nextTaskRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/agents/{agent.Id}/tasks/next");
        nextTaskRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        var nextTask = await _client.SendAsync(nextTaskRequest);
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
            roles = new[] { "provider" },
            acceptMode = "AutoAccept"
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

        using var resultRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{createdTask!.Id}/result")
        {
            Content = JsonContent.Create(new
            {
                success = true,
                result = "Available tomorrow at 14:00"
            })
        };
        resultRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);

        var resultResponse = await _client.SendAsync(resultRequest);
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
            skillDetails = new[] { new { skill = "vendor search" } }
        });
        seekerResponse.EnsureSuccessStatusCode();
        var seeker = await seekerResponse.Content.ReadFromJsonAsync<RegisterResponse>();

        var providerResponse = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Miami Movers Bot",
            roles = new[] { "provider" },
            skillDetails = new[] { 
                new { skill = "moving", location = "Miami" },
                new { skill = "loaders", location = "Miami" }
            }
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

        using var firstMessageRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversation!.Id}/messages")
        {
            Content = JsonContent.Create(new
            {
                fromAgentId = seeker.Id,
                body = "Need movers tomorrow morning in Miami. Are you available?"
            })
        };
        firstMessageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", seeker.ApiKey);
        var firstMessageResponse = await _client.SendAsync(firstMessageRequest);
        firstMessageResponse.EnsureSuccessStatusCode();

        using var secondMessageRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversation.Id}/messages")
        {
            Content = JsonContent.Create(new
            {
                fromAgentId = provider.Id,
                body = "Yes, available after 10:00."
            })
        };
        secondMessageRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        var secondMessageResponse = await _client.SendAsync(secondMessageRequest);
        secondMessageResponse.EnsureSuccessStatusCode();

        using var inboxRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/agents/{provider.Id}/inbox");
        inboxRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        var inboxResponse = await _client.SendAsync(inboxRequest);
        inboxResponse.EnsureSuccessStatusCode();
        var inbox = await inboxResponse.Content.ReadFromJsonAsync<List<ConversationResponse>>();

        Assert.NotNull(inbox);
        var providerConversation = Assert.Single(inbox!);
        Assert.Equal(2, providerConversation.Messages.Length);
        Assert.Equal("Need movers tomorrow morning in Miami. Are you available?", providerConversation.Messages[0].Body);
        Assert.Equal("Yes, available after 10:00.", providerConversation.Messages[1].Body);
    }

    [Fact]
    public async Task AskOwnerFirstProvider_RejectsDirectTaskCreation()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Careful Bot",
            roles = new[] { "provider" },
            acceptMode = "AskOwnerFirst"
        });
        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        var createTask = await _client.PostAsJsonAsync("/api/tasks", new
        {
            targetAgentId = agent!.Id,
            title = "Urgent moving job"
        });

        Assert.Equal(HttpStatusCode.Conflict, createTask.StatusCode);
    }

    [Fact]
    public async Task ProtectedAgentEndpoint_WithoutBearerToken_ReturnsUnauthorized()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Secure Bot",
            roles = new[] { "provider" }
        });
        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        var response = await _client.GetAsync($"/api/agents/{agent!.Id}/tasks/next");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatchProfile_UpdatesScalarFields_KeepsSkillsUnchanged()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Profile Bot",
            roles = new[] { "provider" },
            description = "original",
            skillDetails = new[]
            {
                new { skill = "moving", location = "Miami" },
                new { skill = "packing", location = "Miami" }
            }
        });
        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        using var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/agents/{agent!.Id}/profile")
        {
            Content = JsonContent.Create(new { description = "patched", serviceCategory = "logistics" })
        };
        patchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        var patchResponse = await _client.SendAsync(patchRequest);
        patchResponse.EnsureSuccessStatusCode();

        var getResponse = await _client.GetAsync($"/api/agents/{agent.Id}");
        getResponse.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("patched", root.GetProperty("description").GetString());
        Assert.Equal("logistics", root.GetProperty("serviceCategory").GetString());
        var skillsAfter = root.GetProperty("skillDetails");
        Assert.Equal(2, skillsAfter.GetArrayLength());
        Assert.Equal("Miami", skillsAfter[0].GetProperty("location").GetString());
        Assert.Equal("Miami", skillsAfter[1].GetProperty("location").GetString());
    }

    [Fact]
    public async Task PutSkills_ReplacesSkills_AndClearsWithEmptyArray()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Skills Bot",
            roles = new[] { "provider" },
            skillDetails = new[]
            {
                new { skill = "alpha", location = "A" },
                new { skill = "beta", location = "B" }
            }
        });
        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/agents/{agent!.Id}/skills")
        {
            Content = JsonContent.Create(new
            {
                skillDetails = new[]
                {
                    new { skill = "gamma", currency = "USD", amount = 99m, location = "NYC" }
                }
            })
        };
        putRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        var putResponse = await _client.SendAsync(putRequest);
        putResponse.EnsureSuccessStatusCode();
        using var putDoc = JsonDocument.Parse(await putResponse.Content.ReadAsStringAsync());
        var putSkills = putDoc.RootElement.GetProperty("skillDetails");
        Assert.Equal(1, putSkills.GetArrayLength());
        Assert.Equal("gamma", putSkills[0].GetProperty("skill").GetString());

        using var clearRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/agents/{agent.Id}/skills")
        {
            Content = JsonContent.Create(new { skillDetails = Array.Empty<object>() })
        };
        clearRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        var clearResponse = await _client.SendAsync(clearRequest);
        clearResponse.EnsureSuccessStatusCode();
        using var clearDoc = JsonDocument.Parse(await clearResponse.Content.ReadAsStringAsync());
        Assert.Equal(0, clearDoc.RootElement.GetProperty("skillDetails").GetArrayLength());
    }

    [Fact]
    public async Task PutSkills_DuplicateOrEmptySkillName_ReturnsBadRequest()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Validate Bot",
            roles = new[] { "provider" }
        });
        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        using var dupRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/agents/{agent!.Id}/skills")
        {
            Content = JsonContent.Create(new
            {
                skillDetails = new[]
                {
                    new { skill = "same" },
                    new { skill = "SAME" }
                }
            })
        };
        dupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(dupRequest)).StatusCode);

        using var emptyRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/agents/{agent.Id}/skills")
        {
            Content = JsonContent.Create(new { skillDetails = new[] { new { skill = "  " } } })
        };
        emptyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(emptyRequest)).StatusCode);
    }

    [Fact]
    public async Task PutSkills_WithoutBearer_ReturnsUnauthorized()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Auth Skills Bot",
            roles = new[] { "provider" }
        });
        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        var response = await _client.PutAsJsonAsync($"/api/agents/{agent!.Id}/skills", new
        {
            skillDetails = new[] { new { skill = "x" } }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public sealed record RegisterResponse(Guid Id, string Name, string[] Roles, string ApiKey);
    public sealed record SearchResponse(Guid Id, string Name, string[] Roles);
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
