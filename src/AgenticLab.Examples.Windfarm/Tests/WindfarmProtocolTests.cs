using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgenticLab.AiService.Application.Conversations;
using AgenticLab.AiService.Application.Discovery;
using AgenticLab.Examples.Windfarm.Agents;
using AgenticLab.Examples.Windfarm.Api;
using AgenticLab.Examples.Windfarm.Process;
using AgenticLab.Examples.Windfarm.Protocols;
using AgenticLab.Examples.Windfarm.Tools;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Extensibility.Runtime;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgenticLab.Examples.Windfarm.Tests;

public sealed class WindfarmProtocolTests
{
    [Fact]
    public async Task McpListsWithoutApiThenReadsRealOperationalHttp()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var apiBuilder = CreateBuilder();
        AddBackend(apiBuilder);
        await using var api = apiBuilder.Build();
        api.MapExamples();

        var mcpBuilder = CreateBuilder();
        mcpBuilder.Configuration["AiService:Url"] = "http://127.0.0.1:1";
        mcpBuilder.Services.AddExample<WindfarmExample>(mcpBuilder.Configuration, ExampleHost.Mcp);
        mcpBuilder.Services.AddMcpServer().WithHttpTransport().AddExampleTools(mcpBuilder.Services);
        await using var mcp = mcpBuilder.Build();
        mcp.MapMcp();
        await mcp.StartAsync(deadline.Token);
        await using var provider = new McpToolProvider(Endpoint("Mcp:Endpoint", mcp), NullLogger<McpToolProvider>.Instance);
        await provider.ConnectAsync(deadline.Token);
        Assert.True(provider.IsConnected, provider.Status.Error);
        Assert.Equal(WindfarmMcpTools.Names.Order(), provider.ToolInfos.Select(tool => tool.Name).Order());
        Assert.DoesNotContain(provider.ToolInfos, tool => tool.Name.Contains("Approve") || tool.Name.Contains("Commit"));

        await api.StartAsync(deadline.Token);
        mcpBuilder.Configuration["AiService:Url"] = Assert.Single(api.Urls);
        foreach (var name in WindfarmMcpTools.Names)
        {
            var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(provider.GetTools([name])));
            var result = await tool.InvokeAsync(new AIFunctionArguments { ["scenarioId"] = "inspection" }, deadline.Token);
            var json = JsonSerializer.Serialize(result);
            Assert.True(json.Contains("inspection:", StringComparison.Ordinal), json);
            Assert.Contains("synthetic", json, StringComparison.OrdinalIgnoreCase);
        }
        await provider.ConnectAsync(deadline.Token);
        Assert.Equal(5, provider.GetTools(WindfarmMcpTools.Names).Count);
        Assert.Empty(provider.GetTools(["GetCurrentTime"]));
    }

    [Fact]
    public async Task A2AReviewsAndHumanHttpDecisionCompleteAnIsolatedCase()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var a2aBuilder = CreateBuilder();
        var model = new ReviewModel();
        a2aBuilder.Services.AddSingleton<IChatClient>(model);
        var agents = WindfarmSpecialists.All.Select(specialist =>
        {
            var agent = a2aBuilder.AddAIAgent(specialist.Name, instructions: specialist.Instructions);
            agent.AddA2AServer();
            return (specialist.Name, Agent: agent, specialist.Description);
        }).ToArray();
        await using var remote = a2aBuilder.Build();
        foreach (var agent in agents) remote.MapA2AJsonRpc(agent.Agent, $"/a2a/{agent.Name}");
        remote.MapGet("/agents", () => Results.Ok(new { Agents = agents.Select(agent => new { agent.Name, Path = $"/a2a/{agent.Name}", agent.Description }) }));
        await remote.StartAsync(deadline.Token);
        using var gateway = new A2AAgentProvider(Endpoint("A2A:Endpoint", remote), NullLogger<A2AAgentProvider>.Instance);
        await gateway.ConnectAsync(deadline.Token);

        var builder = CreateBuilder();
        AddBackend(builder, gateway);
        await using var api = builder.Build();
        api.MapExamples();
        await api.StartAsync(deadline.Token);
        using var http = new HttpClient { BaseAddress = new Uri(Assert.Single(api.Urls)) };
        using var created = await http.PostAsJsonAsync($"{WindfarmExample.ApiPath}/cases", new CreateCaseRequest("conversation", "replanning"), deadline.Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        var snapshot = (await created.Content.ReadFromJsonAsync<CaseSnapshot>(deadline.Token))!;
        using var forged = await http.PostAsJsonAsync($"{WindfarmExample.ApiPath}/cases/{snapshot.Id}/decisions",
            new DecisionRequest("conversation", 0, "forged", "approve", "key"), deadline.Token);
        Assert.Equal(HttpStatusCode.Conflict, forged.StatusCode);

        var process = api.Services.GetRequiredService<IWindfarmProcess>();
        var tools = api.Services.GetRequiredService<WindfarmTools>();
        using var scope = new AgentRunScope("conversation", WindfarmCoordinator.AgentName);
        var option = process.Options("conversation").First(candidate => candidate.Eligible);
        tools.WindfarmDraftPlan(option.Id, "Inspect in the later feasible window with the qualified crew.", option.SourceIds.ToArray());
        await Assert.ThrowsAsync<WindfarmException>(() => tools.DelegateToAgent("poet", "Review", deadline.Token));
        foreach (var reviewer in WindfarmProcess.Reviewers)
        {
            var receipt = await tools.DelegateToAgent(reviewer, "Review the draft's evidence and constraints.", deadline.Token);
            Assert.Contains("suppliedSourceIds", receipt);
        }
        Assert.Equal(3, model.Calls);
        tools.WindfarmSubmitProposal();
        snapshot = process.Find("conversation")!;
        Assert.Null(snapshot.Order);
        var request = new DecisionRequest("conversation", snapshot.Revision, snapshot.ProposalHash!, "approve", "human-decision");
        using var approved = await http.PostAsJsonAsync($"{WindfarmExample.ApiPath}/cases/{snapshot.Id}/decisions", request, deadline.Token);
        approved.EnsureSuccessStatusCode();
        var result = (await approved.Content.ReadFromJsonAsync<CaseSnapshot>(deadline.Token))!;
        Assert.Equal(CaseStage.WorkOrderCreated, result.Stage);
        Assert.Equal("crew-alpha", result.Order!.CrewId);
        using var repeated = await http.PostAsJsonAsync($"{WindfarmExample.ApiPath}/cases/{snapshot.Id}/decisions", request, deadline.Token);
        Assert.Equal(result.Order, (await repeated.Content.ReadFromJsonAsync<CaseSnapshot>(deadline.Token))!.Order);
        using var openApi = await http.GetAsync($"{WindfarmExample.ApiPath}/openapi/windfarm.json", deadline.Token);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Contains("decisions", await openApi.Content.ReadAsStringAsync(deadline.Token));
    }

    [Theory]
    [InlineData("not-json", true)]
    [InlineData("{\"observations\":\"OK\",\"concerns\":[],\"sourceIds\":[]}", true)]
    [InlineData("unavailable", false)]
    public async Task MalformedOrFailedSpecialistsCannotCreateReviewReceipts(string response, bool success)
    {
        var process = WindfarmProcessTests.Create();
        process.Create("conversation", "inspection");
        var option = process.Options("conversation").First(candidate => candidate.Eligible);
        process.Draft("conversation", option.Id, "Inspection", option.SourceIds);
        var tools = new WindfarmTools(process, new AgentRunContext(), new StubDelegation(success, response));
        using var scope = new AgentRunScope("conversation", WindfarmCoordinator.AgentName);
        await Assert.ThrowsAsync<WindfarmException>(() => tools.DelegateToAgent(WindfarmProcess.Reviewers[0], "Review"));
        Assert.Empty(process.Find("conversation")!.Reviews);
        Assert.Throws<WindfarmException>(() => process.Submit("conversation"));
    }

    [Fact]
    public void ToolsRequireHostIdentityAndPreserveCaseInsensitiveAgentNames()
    {
        var process = WindfarmProcessTests.Create();
        process.Create("scoped", "inspection");
        var tools = new WindfarmTools(process, new AgentRunContext(), new StubDelegation(false, "offline"));
        Assert.Throws<WindfarmException>(() => tools.WindfarmGetCase());
        using (var otherAgent = new AgentRunScope("scoped", "OtherAgent"))
            Assert.Throws<WindfarmException>(() => tools.WindfarmGetCase());
        using var scope = new AgentRunScope("scoped", "windfarmcoordinator");
        Assert.Contains("\"conversationId\":\"scoped\"", tools.WindfarmGetCase());
        Assert.DoesNotContain(tools.AsTools(), tool => tool.Name.Contains("Approve") || tool.Name.Contains("Decide") || tool.Name.Contains("Commit"));
    }

    [Fact]
    public async Task CancelledReviewCannotRecordAReceipt()
    {
        var process = WindfarmProcessTests.Create();
        process.Create("conversation", "inspection");
        var option = process.Options("conversation").First(candidate => candidate.Eligible);
        process.Draft("conversation", option.Id, "Inspection", option.SourceIds);
        var response = JsonSerializer.Serialize(new ReviewAnswer(ReviewVerdict.Ready, "Review", [], option.SourceIds.ToArray()), WindfarmTools.Json);
        var tools = new WindfarmTools(process, new AgentRunContext(), new StubDelegation(true, response));
        using var scope = new AgentRunScope("conversation", WindfarmCoordinator.AgentName);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.DelegateToAgent(WindfarmProcess.Reviewers[0], "Review", cancellation.Token));
        Assert.Empty(process.Find("conversation")!.Reviews);
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration[$"Examples:{WindfarmExample.Id}:Enabled"] = "true";
        return builder;
    }

    private static void AddBackend(WebApplicationBuilder builder, IAgentDelegation? gateway = null)
    {
        builder.Services.AddSingleton<IAgentRunContext, AgentRunContext>();
        builder.Services.AddSingleton<IMcpToolSource, EmptyMcp>();
        builder.Services.AddSingleton<IAgentDelegation>(gateway ?? new StubDelegation(false, "offline"));
        builder.Services.AddExample<WindfarmExample>(builder.Configuration, ExampleHost.AiService);
    }

    private static IConfiguration Endpoint(string key, WebApplication app) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [key] = Assert.Single(app.Urls) }).Build();

    private sealed class EmptyMcp : IMcpToolSource
    {
        public IList<AITool> GetTools(IReadOnlyCollection<string> names) => [];
    }

    private sealed class StubDelegation(bool success, string text) : IAgentDelegation
    {
        public Task<AgentDelegationResult> InvokeAsync(string agentName, string question, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentDelegationResult(success, text));
    }

    private sealed class ReviewModel : IChatClient
    {
        public int Calls { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            using var document = JsonDocument.Parse(messages.Last(message => message.Role == ChatRole.User).Text);
            var sources = document.RootElement.GetProperty("draft").GetProperty("sourceIds").EnumerateArray().Select(source => source.GetString()!).ToArray();
            var text = JsonSerializer.Serialize(new ReviewAnswer(ReviewVerdict.Ready, "Fixture constraints support a simulated inspection.", [], sources), WindfarmTools.Json);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = ChatFinishReason.Stop });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text) { FinishReason = ChatFinishReason.Stop };
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}