using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Claude.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.Examples.Claude.Tests;

public sealed class ClaudeExampleTests
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
            ["Examples:claude:Enabled"] = enabled.ToString(),
        }).Build();
        using var provider = new ServiceCollection().AddExample<ClaudeExample>(configuration, role).BuildServiceProvider();
        var modules = provider.GetRequiredService<ExampleCatalog>().Modules;
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        if (!enabled)
        {
            Assert.Empty(modules);
            Assert.Empty(provider.GetServices<IVendorHarness>());
            return;
        }

        var manifest = Assert.Single(modules).Manifest;
        Assert.Equal("claude", manifest.Id);
        Assert.Equal(new[] { "claude" }, manifest.HostKeys);
        Assert.Empty(manifest.AgentNames);
        Assert.False(manifest.RequiresUi);
        var presentation = manifest.HostPresentation[ClaudeExample.HostKey];
        Assert.Equal("_content/AgenticLab.Examples.Claude/host.svg", presentation.IconPath);
        Assert.Equal(50, presentation.DisplayOrder);
        Assert.Equal("claude", presentation.ProductConceptId);
        Assert.Contains("Claude", presentation.LegacyKeys);
        if (role == ExampleHost.Web)
        {
            Assert.Empty(provider.GetServices<IVendorHarness>());
            return;
        }

        var harness = Assert.IsType<ClaudeHarness>(Assert.Single(provider.GetServices<IVendorHarness>()));
        Assert.Equal("claude", harness.Key);
        Assert.Equal("Claude", harness.DisplayName);
        Assert.Equal("Claude Sonnet 4.5 (Anthropic)", harness.ModelLabel);
        Assert.Contains("Ground what you say in what the tools return", harness.Harness);
        Assert.Equal(new VendorMode(SharedAgentNames.Chat, "chat"), Assert.Single(harness.Modes));
    }
}