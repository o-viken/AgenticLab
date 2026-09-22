using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Copilot.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.Examples.Copilot.Tests;

public sealed class CopilotExampleTests
{
    [Theory]
    [InlineData(false, ExampleHost.AiService)]
    [InlineData(false, ExampleHost.Web)]
    [InlineData(true, ExampleHost.AiService)]
    [InlineData(true, ExampleHost.Web)]
    public void ContributionsAreOptInAndRoleScoped(bool enabled, ExampleHost role)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Examples:copilot:Enabled"] = enabled.ToString(),
        }).Build();
        using var provider = new ServiceCollection().AddExample<CopilotExample>(configuration, role).BuildServiceProvider();
        var modules = provider.GetRequiredService<ExampleCatalog>().Modules;
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        if (!enabled)
        {
            Assert.Empty(modules);
            Assert.Empty(provider.GetServices<IVendorHarness>());
            return;
        }

        var manifest = Assert.Single(modules).Manifest;
        Assert.Equal("copilot", manifest.Id);
        Assert.Equal(new[] { "copilot" }, manifest.HostKeys);
        Assert.Empty(manifest.AgentNames);
        Assert.False(manifest.RequiresUi);
        var presentation = manifest.HostPresentation[CopilotExample.HostKey];
        Assert.Equal("_content/AgenticLab.Examples.Copilot/host.svg", presentation.IconPath);
        Assert.Equal(30, presentation.DisplayOrder);
        Assert.Equal("github-copilot", presentation.ProductConceptId);
        Assert.Contains("Copilot", presentation.LegacyKeys);
        if (role == ExampleHost.Web)
        {
            Assert.Empty(provider.GetServices<IVendorHarness>());
            return;
        }

        var harness = Assert.IsType<CopilotHarness>(Assert.Single(provider.GetServices<IVendorHarness>()));
        Assert.Equal("copilot", harness.Key);
        Assert.Equal("GitHub Copilot", harness.DisplayName);
        Assert.Equal("GPT-5 (GitHub Copilot)", harness.ModelLabel);
        Assert.Contains("Ground every answer in what the tools return", harness.Harness);
        Assert.Equal(new[] { new VendorMode(SharedAgentNames.Ask, "ask"),
            new VendorMode(SharedAgentNames.Plan, "plan"), new VendorMode(SharedAgentNames.Coder, "agent") }, harness.Modes);
    }
}