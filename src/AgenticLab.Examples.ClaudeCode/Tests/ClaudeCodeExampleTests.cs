using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.ClaudeCode.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.Examples.ClaudeCode.Tests;

public sealed class ClaudeCodeExampleTests
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
            ["Examples:claude-code:Enabled"] = enabled.ToString(),
        }).Build();
        using var provider = new ServiceCollection().AddExample<ClaudeCodeExample>(configuration, role).BuildServiceProvider();
        var modules = provider.GetRequiredService<ExampleCatalog>().Modules;
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        if (!enabled)
        {
            Assert.Empty(modules);
            Assert.Empty(provider.GetServices<IVendorHarness>());
            return;
        }

        var manifest = Assert.Single(modules).Manifest;
        Assert.Equal("claude-code", manifest.Id);
        Assert.Equal(new[] { "claude-code" }, manifest.HostKeys);
        Assert.Empty(manifest.AgentNames);
        Assert.False(manifest.RequiresUi);
        var presentation = manifest.HostPresentation[ClaudeCodeExample.HostKey];
        Assert.Equal("_content/AgenticLab.Examples.ClaudeCode/host.svg", presentation.IconPath);
        Assert.Equal(40, presentation.DisplayOrder);
        Assert.Equal("claude-code", presentation.ProductConceptId);
        Assert.Contains("ClaudeCode", presentation.LegacyKeys);
        if (role == ExampleHost.Web)
        {
            Assert.Empty(provider.GetServices<IVendorHarness>());
            return;
        }

        var harness = Assert.IsType<ClaudeCodeHarness>(Assert.Single(provider.GetServices<IVendorHarness>()));
        Assert.Equal("claude-code", harness.Key);
        Assert.Equal("Claude Code", harness.DisplayName);
        Assert.Equal("Claude Sonnet 4.5 (Anthropic)", harness.ModelLabel);
        Assert.Contains("verify your work with the tools rather than assuming", harness.Harness);
        Assert.Equal(new[] { new VendorMode(SharedAgentNames.Plan, "plan"),
            new VendorMode(SharedAgentNames.Coder, "agent") }, harness.Modes);
    }
}