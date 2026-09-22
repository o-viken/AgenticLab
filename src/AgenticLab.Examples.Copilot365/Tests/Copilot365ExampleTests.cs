using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Extensibility.Runtime;
using AgenticLab.Examples.Copilot365.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.Examples.Copilot365.Tests;

public sealed class Copilot365ExampleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void DisabledModuleContributesNothingAndNeedsNoHostTools(bool? enabled)
    {
        using var provider = new ServiceCollection()
            .AddExample<Copilot365Example>(Configuration(enabled), ExampleHost.AiService)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.Empty(provider.GetRequiredService<ExampleCatalog>().Modules);
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        Assert.Empty(provider.GetServices<IVendorHarness>());
        Assert.Null(provider.GetService<Microsoft365Tool>());
        Assert.Null(provider.GetService<IHostToolSource>());
    }

    [Fact]
    public void EnabledModulePreservesAgentNamesModesAndExactToolSubsets()
    {
        var hostTools = new StubHostTools();
        using var provider = new ServiceCollection().AddSingleton<IHostToolSource>(hostTools)
            .AddExample<Copilot365Example>(Configuration(true), ExampleHost.AiService)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var agents = provider.GetServices<IAgentDefinition>().ToArray();
        Assert.Equal(new[] { "M365Copilot", "M365Researcher", "M365Analyst" }, agents.Select(agent => agent.Name));
        Assert.Equal(new[] { "SearchEmail", "SearchFiles", "SearchChats", "GetCalendar", "FindPeople", "SummarizeDocument", "SendMail" },
            agents[0].Tools.Select(tool => tool.Name));
        Assert.Equal(new[] { "SearchEmail", "SearchFiles", "SearchChats", "FindPeople", "SummarizeDocument", "SearchWiki", "GetWikiPage" },
            agents[1].Tools.Select(tool => tool.Name));
        Assert.Equal(new[] { "SearchFiles", "SummarizeDocument", "Calculate" }, agents[2].Tools.Select(tool => tool.Name));
        Assert.Equal(new[] { AgentRiskLevel.Medium, AgentRiskLevel.Low, AgentRiskLevel.Low }, agents.Select(agent => agent.RiskLevel));
        Assert.Equal(new[] { "SearchWiki", "GetWikiPage", "Calculate" }, hostTools.Requested);
        Assert.All(agents, agent => Assert.False(agent.RequiresWorkspace));

        var harness = Assert.Single(provider.GetServices<IVendorHarness>());
        Assert.Equal("microsoft365", harness.Key);
        Assert.Equal("Copilot 365", harness.DisplayName);
        Assert.Equal(new[] { "chat", "researcher", "analyst" }, harness.Modes.Select(mode => mode.Label));
        Assert.Equal(agents.Select(agent => agent.Name), harness.Modes.Select(mode => mode.Agent));
        Assert.Contains("Microsoft 365 Copilot", harness.Harness);
        var catalog = provider.GetRequiredService<ExampleCatalog>();
        Assert.All(agents, agent => Assert.Equal("copilot365", catalog.ForAgent(agent.Name)?.Id));
        Assert.Same(provider.GetRequiredService<Microsoft365Tool>(), provider.GetRequiredService<Microsoft365Tool>());
    }

    [Fact]
    public void WebRegistrationProvidesManifestWithoutBackendServicesOrPanel()
    {
        using var provider = new ServiceCollection()
            .AddExample<Copilot365Example>(Configuration(true), ExampleHost.Web)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var module = Assert.Single(provider.GetRequiredService<ExampleCatalog>().Modules);
        Assert.False(module is IWebExample);
        Assert.False(module.Manifest.RequiresUi);
        Assert.Equal("copilot365", module.Manifest.Id);
        Assert.Equal("Copilot 365", module.Manifest.DisplayName);
        Assert.Equal("microsoft365", Assert.Single(module.Manifest.HostKeys));
        var presentation = module.Manifest.HostPresentation[Copilot365Example.HostKey];
        Assert.Equal("_content/AgenticLab.Examples.Copilot365/host.svg", presentation.IconPath);
        Assert.Equal(60, presentation.DisplayOrder);
        Assert.Equal("microsoft-365-copilot", presentation.ProductConceptId);
        Assert.Contains("Microsoft365", presentation.LegacyKeys);
        Assert.Equal(3, module.Manifest.AgentNames.Count);
        Assert.Empty(module.Manifest.McpToolNames);
        Assert.Empty(module.Manifest.RemoteAgentNames);
        var resource = Assert.Single(module.Manifest.Resources);
        Assert.Equal("microsoft365", resource.Key);
        Assert.Equal(new Microsoft365Tool().AsTools().Select(tool => tool.Name), resource.ToolNames);
        var risk = Assert.Single(module.Manifest.ToolRisks);
        Assert.Equal("SendMail", risk.Key);
        Assert.Equal("Medium", risk.Value.Level);
        Assert.Contains("never sends real email", risk.Value.Description);
        Assert.Null(provider.GetService<Microsoft365Tool>());
        Assert.Null(provider.GetService<IHostToolSource>());
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        Assert.Empty(provider.GetServices<IVendorHarness>());
    }

    [Fact]
    public void SyntheticWorkContentKeepsCrossReferencedEvidence()
    {
        var tools = new Microsoft365Tool();
        Assert.Contains("Priya Shah", tools.SearchEmail("Q3"));
        Assert.Contains("FY26 Budget.xlsx", tools.SearchFiles("BUDGET"));
        Assert.Contains("Aisha Okoro", tools.SearchChats("SSO"));
        Assert.Contains("priya.shah@contoso.com", tools.FindPeople("Priya"));
        Assert.Contains("$4.2M", tools.SummarizeDocument("FY26 Budget"));
        Assert.Equal(4, tools.GetCalendar().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("Q3 launch go/no-go", tools.GetCalendar("launch"));
        Assert.DoesNotContain("Finance forecast", tools.GetCalendar("launch"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonexistent-sample-content")]
    public void MissingSearchEvidenceDoesNotInventContent(string query)
    {
        var tools = new Microsoft365Tool();
        Assert.StartsWith("No work emails matched", tools.SearchEmail(query));
        Assert.StartsWith("No files matched", tools.SearchFiles(query));
        Assert.StartsWith("No Teams messages matched", tools.SearchChats(query));
        Assert.StartsWith("No people matched", tools.FindPeople(query));
        Assert.StartsWith("No document matched", tools.SummarizeDocument(query));
    }

    [Theory]
    [InlineData("", "Launch", "Message")]
    [InlineData("not-an-address", "Launch", "Message")]
    [InlineData("priya.shah@contoso.com", "", " ")]
    public void SimulatedMailRejectsInvalidInputs(string recipient, string subject, string body)
    {
        Assert.StartsWith("Refused to send:", new Microsoft365Tool().SendMail(recipient, subject, body));
    }

    [Fact]
    public void SimulatedMailReturnsReceiptWithoutChangingTheFixture()
    {
        var tools = new Microsoft365Tool();
        var before = tools.SearchEmail("Q3");
        var receipt = tools.SendMail("priya.shah@contoso.com", "Q3 follow-up", "Please review the launch plan.");
        Assert.Contains("Sent email to priya.shah@contoso.com", receipt);
        Assert.Contains("Q3 follow-up", receipt);
        Assert.Equal(before, new Microsoft365Tool().SearchEmail("Q3"));
    }

    private static IConfiguration Configuration(bool? enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Examples:{Copilot365Example.Id}:Enabled"] = enabled?.ToString(),
        }).Build();

    private sealed class StubHostTools : IHostToolSource
    {
        private readonly Dictionary<string, AITool> _tools = new[] { "SearchWiki", "GetWikiPage", "Calculate" }
            .ToDictionary(name => name, name => (AITool)AIFunctionFactory.Create(() => "stub", name), StringComparer.Ordinal);

        public List<string> Requested { get; } = [];

        public IList<AITool> GetTools(IReadOnlyCollection<string> names)
        {
            Requested.AddRange(names);
            return names.Select(name => _tools[name]).ToList();
        }
    }
}