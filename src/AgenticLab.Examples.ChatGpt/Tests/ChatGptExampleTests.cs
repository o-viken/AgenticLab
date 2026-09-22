using System.Text.Json;
using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Extensibility.Runtime;
using AgenticLab.Examples.ChatGpt.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.Examples.ChatGpt.Tests;

public sealed class ChatGptExampleTests
{
    [Theory]
    [InlineData(ExampleHost.AiService)]
    [InlineData(ExampleHost.Web)]
    public void DisabledModuleContributesNothing(ExampleHost role)
    {
        using var provider = new ServiceCollection().AddExample<ChatGptExample>(Configuration(false), role)
            .BuildServiceProvider();
        Assert.Empty(provider.GetRequiredService<ExampleCatalog>().Modules);
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        Assert.Empty(provider.GetServices<IVendorHarness>());
    }

    [Fact]
    public void EnabledBackendKeepsAgentIdentityAndBoundedToolSelection()
    {
        var tools = new TestTools();
        using var provider = new ServiceCollection().AddSingleton<IHostToolSource>(tools)
            .AddExample<ChatGptExample>(Configuration(true), ExampleHost.AiService).BuildServiceProvider();
        var agent = Assert.IsType<ChatGptAgent>(Assert.Single(provider.GetServices<IAgentDefinition>()));
        var harness = Assert.IsType<ChatGptHarness>(Assert.Single(provider.GetServices<IVendorHarness>()));
        Assert.Equal("ChatGpt", agent.Name);
        Assert.Equal("chatgpt", harness.Key);
        Assert.Equal("ChatGPT", harness.DisplayName);
        Assert.Equal("GPT-5 (OpenAI)", harness.ModelLabel);
        Assert.Equal(new VendorMode(agent.Name, "chat"), Assert.Single(harness.Modes));
        Assert.Contains("Ground your answers in what the tools return", harness.Harness);
        Assert.Equal(AgentRiskLevel.Low, agent.RiskLevel);
        Assert.False(agent.RequiresWorkspace);
        Assert.False(agent.SupportsSkills);
        Assert.Contains("No file-system or command access", agent.Guardrails);
        var selected = agent.Tools;
        Assert.Equal(new[] { "SearchWiki", "GetWikiPage", "Calculate" }, selected.Select(tool => tool.Name));
        Assert.Equal(selected.Select(tool => tool.Name), tools.Requested);
        foreach (var tool in selected) Assert.Same(tools.Published[tool.Name], tool);
        Assert.Same(tools, Assert.Single(provider.GetServices<IHostToolSource>()));
        Assert.Equal(ChatGptExample.Id, provider.GetRequiredService<ExampleCatalog>().ForAgent(agent.Name)?.Id);
    }

    [Fact]
    public async Task SelectedToolsRemainExecutable()
    {
        var agent = new ChatGptAgent(new TestTools());
        foreach (var tool in agent.Tools.Cast<AIFunction>())
        {
            var result = await tool.InvokeAsync(new AIFunctionArguments());
            Assert.Equal("sample result", Assert.IsType<JsonElement>(result).GetString());
        }
    }

    [Fact]
    public void WebRolePublishesBrandingWithoutBackendServices()
    {
        using var provider = new ServiceCollection().AddExample<ChatGptExample>(Configuration(true), ExampleHost.Web)
            .BuildServiceProvider();
        var manifest = Assert.Single(provider.GetRequiredService<ExampleCatalog>().Modules).Manifest;
        Assert.Equal(new[] { "ChatGpt" }, manifest.AgentNames);
        Assert.Equal(new[] { "chatgpt" }, manifest.HostKeys);
        Assert.False(manifest.RequiresUi);
        var presentation = manifest.HostPresentation[ChatGptExample.HostKey];
        Assert.Equal("_content/AgenticLab.Examples.ChatGpt/host.svg", presentation.IconPath);
        Assert.Equal(10, presentation.DisplayOrder);
        Assert.Equal("chatgpt", presentation.ProductConceptId);
        Assert.Contains("ChatGpt", presentation.LegacyKeys);
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        Assert.Empty(provider.GetServices<IVendorHarness>());
    }

    private static IConfiguration Configuration(bool enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Examples:chatgpt:Enabled"] = enabled.ToString() }).Build();

    private sealed class TestTools : IHostToolSource
    {
        public IReadOnlyDictionary<string, AITool> Published { get; } = new[] { "SearchWiki", "GetWikiPage", "Calculate" }
            .ToDictionary(name => name, name => (AITool)AIFunctionFactory.Create(() => "sample result", name));
        public IReadOnlyCollection<string> Requested { get; private set; } = [];

        public IList<AITool> GetTools(IReadOnlyCollection<string> names)
        {
            Requested = names;
            return names.Select(name => Published[name]).ToList();
        }
    }
}