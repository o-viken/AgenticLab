using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TheSeries.AiService.Application.Discovery;
using TheSeries.McpServer.Tools;
using Xunit;

namespace TheSeries.AiService.Tests;

public sealed class ProtocolIntegrationTests
{
    [Fact]
    public async Task McpDiscoveryAndRediscoveryExposeCallableTimeToolOverHttp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var builder = CreateBuilder();
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<TimeTools>();
        await using var server = builder.Build();
        server.MapMcp();
        await server.StartAsync(timeout.Token);
        var configuration = EndpointConfiguration("Mcp:Endpoint", server);
        await using var provider = new McpToolProvider(configuration, NullLogger<McpToolProvider>.Instance);

        for (var discovery = 0; discovery < 2; discovery++)
        {
            await provider.ConnectAsync(timeout.Token);

            Assert.True(provider.IsConnected, provider.Status.Error);
            Assert.Equal(DiscoveryState.Connected, provider.Status.State);
            Assert.Equal("GetCurrentTime", Assert.Single(provider.ToolInfos).Name);
            var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(provider.GetTools()));
            var result = await tool.InvokeAsync(new AIFunctionArguments(), timeout.Token);
            Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", JsonSerializer.Serialize(result));
        }
    }

    [Fact]
    public async Task A2ADiscoveryAndDelegationReturnTheHostedAgentsAnswerOverHttp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var builder = CreateBuilder();
        var model = new EchoModel();
        builder.Services.AddSingleton<IChatClient>(model);
        var agent = builder.AddAIAgent("research", instructions: "Echo the question.");
        agent.AddA2AServer();
        await using var server = builder.Build();
        server.MapA2AJsonRpc(agent, "/a2a/research");
        server.MapGet("/agents", () => Results.Ok(new
        {
            Agents = new[] { new { Name = "research", Path = "/a2a/research", Description = "Test specialist" } },
        }));
        await server.StartAsync(timeout.Token);
        var configuration = EndpointConfiguration("A2A:Endpoint", server);
        using var provider = new A2AAgentProvider(configuration, NullLogger<A2AAgentProvider>.Instance);

        for (var discovery = 0; discovery < 2; discovery++)
        {
            await provider.ConnectAsync(timeout.Token);

            Assert.True(provider.IsConnected, provider.Status.Error);
            Assert.Equal(DiscoveryState.Connected, provider.Status.State);
            Assert.Equal("research", Assert.Single(provider.AgentInfos).Name);
            var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(provider.GetTools()));
            var result = await tool.InvokeAsync(new AIFunctionArguments
            {
                ["agentName"] = "research",
                ["question"] = "What is the test answer?",
            }, timeout.Token);
            Assert.Equal("Answer: What is the test answer?", result?.ToString());
        }
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        return builder;
    }

    private static IConfiguration EndpointConfiguration(string key, WebApplication server) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = Assert.Single(server.Urls),
        }).Build();

    private sealed class EchoModel : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer(messages)))
            {
                ResponseId = Guid.NewGuid().ToString(),
                FinishReason = ChatFinishReason.Stop,
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, Answer(messages))
            {
                ResponseId = Guid.NewGuid().ToString(),
                FinishReason = ChatFinishReason.Stop,
            };
        }

        private static string Answer(IEnumerable<ChatMessage> messages) =>
            $"Answer: {messages.Last(message => message.Role == ChatRole.User).Text}";

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}