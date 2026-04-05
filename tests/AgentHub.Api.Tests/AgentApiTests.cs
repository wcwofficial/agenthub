using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    public async Task AgentOnboarding_ReturnsJson_WithChecklists()
    {
        var response = await _client.GetAsync("/api/meta/agent-onboarding");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("agenthub", root.GetProperty("platform").GetString());
        Assert.True(root.GetProperty("askOwnerBeforeRegister").TryGetProperty("ru", out var ru));
        Assert.True(ru.GetArrayLength() > 0);
        Assert.True(root.GetProperty("discovery").TryGetProperty("landingPage", out _));
        var integratorDoc = root.GetProperty("discovery").GetProperty("integratorDoc").GetString();
        Assert.NotNull(integratorDoc);
        Assert.Contains("/AGENT_INTEGRATORS.md", integratorDoc);
        var skillFull = root.GetProperty("discovery").GetProperty("openClawSkillFull").GetString();
        Assert.NotNull(skillFull);
        Assert.Contains("openclaw-agenthub-skill.md", skillFull);

        var wellKnown = await _client.GetAsync("/.well-known/agenthub.json");
        wellKnown.EnsureSuccessStatusCode();
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
    public async Task ClaimTask_RequiresTargetAgentBearer()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Claim Bot",
            roles = new[] { "provider" },
            acceptMode = "AutoAccept"
        });
        register.EnsureSuccessStatusCode();
        var worker = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(worker);

        var taskCreate = await _client.PostAsJsonAsync("/api/tasks", new
        {
            targetAgentId = worker!.Id,
            title = "Claimable task"
        });
        taskCreate.EnsureSuccessStatusCode();
        var created = await taskCreate.Content.ReadFromJsonAsync<CreateTaskResponse>();
        Assert.NotNull(created);

        var claimNoAuth = await _client.PostAsync($"/api/tasks/{created!.Id}/claim", null);
        Assert.Equal(HttpStatusCode.Unauthorized, claimNoAuth.StatusCode);

        using var claimOk = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{created!.Id}/claim");
        claimOk.Headers.Authorization = new AuthenticationHeaderValue("Bearer", worker.ApiKey);
        var claimRes = await _client.SendAsync(claimOk);
        claimRes.EnsureSuccessStatusCode();

        var task = await claimRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);
        Assert.Equal("Claimed", task!.Status);
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

        using var claimRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{createdTask!.Id}/claim");
        claimRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        (await _client.SendAsync(claimRequest)).EnsureSuccessStatusCode();

        using var resultRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{createdTask.Id}/result")
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
    public async Task NeverAutoProvider_BlockTaskCreation_ReturnsConflict()
    {
        var reg = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Closed Bot",
            roles = new[] { "provider" },
            acceptMode = "NeverAuto"
        });
        reg.EnsureSuccessStatusCode();
        var agent = await reg.Content.ReadFromJsonAsync<RegisterResponse>();

        var createTask = await _client.PostAsJsonAsync("/api/tasks", new
        {
            targetAgentId = agent!.Id,
            title = "Nope"
        });
        Assert.Equal(HttpStatusCode.Conflict, createTask.StatusCode);
    }

    [Fact]
    public async Task AskOwnerFirst_CreatesAwaiting_ThenAcceptClaim_Completes()
    {
        var providerReg = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Careful Bot",
            roles = new[] { "provider" },
            acceptMode = "AskOwnerFirst"
        });
        providerReg.EnsureSuccessStatusCode();
        var provider = await providerReg.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(provider);

        var seekerReg = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Seeker Bot",
            roles = new[] { "seeker" }
        });
        seekerReg.EnsureSuccessStatusCode();
        var seeker = await seekerReg.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(seeker);

        var createTask = await _client.PostAsJsonAsync("/api/tasks", new
        {
            fromAgentId = seeker!.Id,
            targetAgentId = provider!.Id,
            title = "Urgent moving job"
        });
        createTask.EnsureSuccessStatusCode();
        var created = await createTask.Content.ReadFromJsonAsync<CreateTaskResponse>();
        Assert.NotNull(created);
        Assert.Equal("AwaitingTargetAcceptance", created!.Status);

        using var next1 = new HttpRequestMessage(HttpMethod.Get, $"/api/agents/{provider.Id}/tasks/next");
        next1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        var nextRes = await _client.SendAsync(next1);
        nextRes.EnsureSuccessStatusCode();
        var peek = await nextRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(peek);
        Assert.Equal("AwaitingTargetAcceptance", peek!.Status);

        var claimTooEarly = await _client.PostAsync($"/api/tasks/{created.Id}/claim", null);
        Assert.Equal(HttpStatusCode.Unauthorized, claimTooEarly.StatusCode);

        using var claimBad = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{created.Id}/claim");
        claimBad.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(claimBad)).StatusCode);

        using var acceptReq = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{created.Id}/accept");
        acceptReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        (await _client.SendAsync(acceptReq)).EnsureSuccessStatusCode();

        using var claimOk = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{created.Id}/claim");
        claimOk.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        (await _client.SendAsync(claimOk)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AskOwnerFirst_Decline_EndsInDeclined()
    {
        var providerReg = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Decline Bot",
            roles = new[] { "provider" },
            acceptMode = "AskOwnerFirst"
        });
        providerReg.EnsureSuccessStatusCode();
        var provider = await providerReg.Content.ReadFromJsonAsync<RegisterResponse>();

        var createTask = await _client.PostAsJsonAsync("/api/tasks", new
        {
            targetAgentId = provider!.Id,
            title = "No thanks"
        });
        createTask.EnsureSuccessStatusCode();
        var created = await createTask.Content.ReadFromJsonAsync<CreateTaskResponse>();
        Assert.NotNull(created);

        using var declineReq = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{created!.Id}/decline")
        {
            Content = JsonContent.Create(new { reason = "Busy" })
        };
        declineReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        var declineRes = await _client.SendAsync(declineReq);
        declineRes.EnsureSuccessStatusCode();
        var body = await declineRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(body);
        Assert.Equal("Declined", body!.Status);
    }

    [Fact]
    public async Task Cancel_BySeeker_Works_WhenAwaiting()
    {
        var seekerReg = await _client.PostAsJsonAsync("/api/agents/register", new { name = "Cancel S", roles = new[] { "seeker" } });
        seekerReg.EnsureSuccessStatusCode();
        var seeker = await seekerReg.Content.ReadFromJsonAsync<RegisterResponse>();

        var providerReg = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Cancel P",
            roles = new[] { "provider" },
            acceptMode = "AskOwnerFirst"
        });
        providerReg.EnsureSuccessStatusCode();
        var provider = await providerReg.Content.ReadFromJsonAsync<RegisterResponse>();

        var createTask = await _client.PostAsJsonAsync("/api/tasks", new
        {
            fromAgentId = seeker!.Id,
            targetAgentId = provider!.Id,
            title = "Never mind"
        });
        createTask.EnsureSuccessStatusCode();
        var created = await createTask.Content.ReadFromJsonAsync<CreateTaskResponse>();
        Assert.NotNull(created);

        using var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{created!.Id}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", seeker.ApiKey);
        var cancelRes = await _client.SendAsync(cancelReq);
        cancelRes.EnsureSuccessStatusCode();
        var t = await cancelRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Equal("Cancelled", t!.Status);
    }

    [Fact]
    public async Task SubmitTaskResult_WithoutClaim_ReturnsBadRequest()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "No Claim Bot",
            roles = new[] { "provider" },
            acceptMode = "AutoAccept"
        });
        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();

        var taskCreate = await _client.PostAsJsonAsync("/api/tasks", new
        {
            targetAgentId = agent!.Id,
            title = "Skip claim"
        });
        taskCreate.EnsureSuccessStatusCode();
        var created = await taskCreate.Content.ReadFromJsonAsync<CreateTaskResponse>();

        using var resultRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{created!.Id}/result")
        {
            Content = JsonContent.Create(new { success = true, result = "nope" })
        };
        resultRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        var resultResponse = await _client.SendAsync(resultRequest);
        Assert.Equal(HttpStatusCode.BadRequest, resultResponse.StatusCode);
    }

    [Fact]
    public async Task SelfDelete_WithoutBearer_ReturnsUnauthorized()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Leaving Bot",
            roles = new[] { "seeker" }
        });
        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        var response = await _client.DeleteAsync($"/api/agents/{agent!.Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SelfDelete_WithBearer_RemovesProfile_ReturnsNoContent()
    {
        var register = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Gone Bot",
            roles = new[] { "provider" }
        });
        register.EnsureSuccessStatusCode();
        var agent = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(agent);

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/agents/{agent!.Id}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var get = await _client.GetAsync($"/api/agents/{agent.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task SelfDelete_RemovesTasks_LinkedAsTargetOrSource()
    {
        var seekerReg = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Seeker X",
            roles = new[] { "seeker" }
        });
        seekerReg.EnsureSuccessStatusCode();
        var seeker = await seekerReg.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(seeker);

        var providerReg = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Provider X",
            roles = new[] { "provider" },
            acceptMode = "AutoAccept"
        });
        providerReg.EnsureSuccessStatusCode();
        var provider = await providerReg.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(provider);

        var taskReg = await _client.PostAsJsonAsync("/api/tasks", new
        {
            fromAgentId = seeker!.Id,
            targetAgentId = provider!.Id,
            title = "Job"
        });
        taskReg.EnsureSuccessStatusCode();
        var created = await taskReg.Content.ReadFromJsonAsync<CreateTaskResponse>();
        Assert.NotNull(created);

        using var deleteSeeker = new HttpRequestMessage(HttpMethod.Delete, $"/api/agents/{seeker.Id}");
        deleteSeeker.Headers.Authorization = new AuthenticationHeaderValue("Bearer", seeker.ApiKey);
        var delSeekerResp = await _client.SendAsync(deleteSeeker);
        delSeekerResp.EnsureSuccessStatusCode();

        using var deleteProvider = new HttpRequestMessage(HttpMethod.Delete, $"/api/agents/{provider.Id}");
        deleteProvider.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        var delProvResp = await _client.SendAsync(deleteProvider);
        delProvResp.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/agents/{seeker.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/agents/{provider.Id}")).StatusCode);
    }

    [Fact]
    public async Task SelfDelete_InThreePartyConversation_KeepsThreadWithRemainingParticipants()
    {
        var regs = new List<RegisterResponse>();
        for (var i = 0; i < 3; i++)
        {
            var r = await _client.PostAsJsonAsync("/api/agents/register", new
            {
                name = $"Party {i}",
                roles = new[] { "seeker" }
            });
            r.EnsureSuccessStatusCode();
            var a = await r.Content.ReadFromJsonAsync<RegisterResponse>();
            Assert.NotNull(a);
            regs.Add(a);
        }

        var convResp = await _client.PostAsJsonAsync("/api/conversations", new
        {
            subject = "group",
            participantAgentIds = new[] { regs[0].Id, regs[1].Id, regs[2].Id }
        });
        convResp.EnsureSuccessStatusCode();
        var conv = await convResp.Content.ReadFromJsonAsync<ConversationResponse>();
        Assert.NotNull(conv);

        using var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/agents/{regs[1].Id}");
        del.Headers.Authorization = new AuthenticationHeaderValue("Bearer", regs[1].ApiKey);
        (await _client.SendAsync(del)).EnsureSuccessStatusCode();

        var getConv = await _client.GetAsync($"/api/conversations/{conv!.Id}");
        getConv.EnsureSuccessStatusCode();
        var after = await getConv.Content.ReadFromJsonAsync<ConversationResponse>();
        Assert.NotNull(after);
        Assert.Equal(2, after!.ParticipantAgentIds.Length);
        Assert.Contains(regs[0].Id, after.ParticipantAgentIds);
        Assert.Contains(regs[2].Id, after.ParticipantAgentIds);
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

public class AgentHubRegistrationKeyTests : IClassFixture<AgentHubApiFactoryWithRegistrationKey>
{
    private readonly HttpClient _client;

    public AgentHubRegistrationKeyTests(AgentHubApiFactoryWithRegistrationKey factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Register_WithoutHeader_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/agents/register", new
        {
            name = "Gated Bot",
            roles = new[] { "provider" }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithWrongKey_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/agents/register")
        {
            Content = JsonContent.Create(new { name = "Gated Bot", roles = new[] { "provider" } })
        };
        request.Headers.Add(AgentHubAuth.RegistrationKeyHeader, "wrong");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithConfiguredKey_Succeeds()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/agents/register")
        {
            Content = JsonContent.Create(new { name = "Gated Bot", roles = new[] { "provider" } })
        };
        request.Headers.Add(AgentHubAuth.RegistrationKeyHeader, "integration-test-reg-key");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AgentApiTests.RegisterResponse>();
        Assert.NotNull(body);
    }
}

public class AgentHubApiFactoryWithRegistrationKey : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentHub:RegistrationApiKey"] = "integration-test-reg-key"
            });
        });

        builder.ConfigureServices(services =>
        {
            var existing = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AgentHubDbContext>));
            if (existing is not null)
                services.Remove(existing);

            services.AddDbContext<AgentHubDbContext>(options =>
                options.UseInMemoryDatabase("agenthub-tests-regkey"));
        });
    }
}
