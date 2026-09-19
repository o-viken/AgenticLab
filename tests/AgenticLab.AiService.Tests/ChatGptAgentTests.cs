using System.Net;
using Microsoft.Extensions.AI;
using AgenticLab.AiService.Application.Agents;
using AgenticLab.AiService.Demo.Agents;
using AgenticLab.AiService.Demo.Tools;
using AgenticLab.AiService.Demo.Vendors;
using Xunit;

namespace AgenticLab.AiService.Tests;

/// <summary>Checks the ChatGPT demo's tool composition without model or network dependencies.</summary>
public sealed class ChatGptAgentTests
{
    [Fact]
    public void ChatMode_UsesDedicatedLowRiskAgent_LeavingPlainChatToolFree()
    {
        using var http = new HttpClient();
        var agent = new ChatGptAgent(new WikiTool(http), new CalculatorTool());

        var mode = Assert.Single(new ChatGptHarness().Modes);
        Assert.Equal(agent.Name, mode.Agent);
        Assert.Equal("chat", mode.Label);
        Assert.Equal(AgentRiskLevel.Low, agent.RiskLevel);
        Assert.False(agent.RequiresWorkspace);
        Assert.False(agent.SupportsSkills);
        Assert.Equal(new[] { "SearchWiki", "GetWikiPage", "Calculate" },
            agent.Tools.OfType<AIFunction>().Select(tool => tool.Name));
        Assert.Empty(new ChatAgent().Tools);
        Assert.Equal(ChatAgent.AgentName, Assert.Single(new ClaudeHarness().Modes).Agent);
        Assert.Equal(ChatAgent.AgentName, Assert.Single(new GeminiHarness().Modes).Agent);
    }

    [Fact]
    public async Task ExposedTools_CanLookUpFactsAndCalculate()
    {
        using var handler = new WikipediaHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://en.wikipedia.org") };
        var agent = new ChatGptAgent(new WikiTool(http), new CalculatorTool());
        var tools = agent.Tools.OfType<AIFunction>().ToDictionary(tool => tool.Name);

        var search = await tools["SearchWiki"].InvokeAsync(new AIFunctionArguments { ["query"] = "Eiffel Tower" });
        var page = await tools["GetWikiPage"].InvokeAsync(new AIFunctionArguments { ["title"] = "Eiffel Tower" });
        var calculation = await tools["Calculate"].InvokeAsync(new AIFunctionArguments { ["expression"] = "330 - 250" });

        Assert.Contains("Eiffel Tower", search!.ToString());
        Assert.Contains("330 metres", page!.ToString());
        Assert.Equal("80", calculation!.ToString());
        Assert.Equal(2, handler.Requests);
    }

    private sealed class WikipediaHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal("en.wikipedia.org", request.RequestUri!.Host);
            var path = request.RequestUri.AbsolutePath;
            var body = path switch
            {
                "/w/rest.php/v1/search/page" => """{"pages":[{"title":"Eiffel Tower","description":"Tower in Paris"}]}""",
                "/api/rest_v1/page/summary/Eiffel%20Tower" => """{"extract":"The Eiffel Tower is 330 metres tall."}""",
                _ => throw new InvalidOperationException($"Unexpected Wikipedia path: {path}"),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}