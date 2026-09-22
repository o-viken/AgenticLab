using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Gemini.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.Examples.Gemini.Tests;

public sealed class GeminiExampleTests
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
            ["Examples:gemini:Enabled"] = enabled.ToString(),
        }).Build();
        using var provider = new ServiceCollection().AddExample<GeminiExample>(configuration, role).BuildServiceProvider();
        var modules = provider.GetRequiredService<ExampleCatalog>().Modules;
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        if (!enabled)
        {
            Assert.Empty(modules);
            Assert.Empty(provider.GetServices<IVendorHarness>());
            return;
        }

        var manifest = Assert.Single(modules).Manifest;
        Assert.Equal("gemini", manifest.Id);
        Assert.Equal(new[] { "gemini" }, manifest.HostKeys);
        Assert.Empty(manifest.AgentNames);
        Assert.False(manifest.RequiresUi);
        var presentation = manifest.HostPresentation[GeminiExample.HostKey];
        Assert.Equal("_content/AgenticLab.Examples.Gemini/host.svg", presentation.IconPath);
        Assert.Equal(20, presentation.DisplayOrder);
        Assert.Equal("gemini", presentation.ProductConceptId);
        Assert.Contains("Gemini", presentation.LegacyKeys);
        if (role == ExampleHost.Web)
        {
            Assert.Empty(provider.GetServices<IVendorHarness>());
            return;
        }

        var harness = Assert.IsType<GeminiHarness>(Assert.Single(provider.GetServices<IVendorHarness>()));
        Assert.Equal("gemini", harness.Key);
        Assert.Equal("Gemini", harness.DisplayName);
        Assert.Equal("Gemini 2.5 Pro (Google)", harness.ModelLabel);
        Assert.Contains("base your answers on what the tools actually return", harness.Harness);
        Assert.Equal(new VendorMode(SharedAgentNames.Chat, "chat"), Assert.Single(harness.Modes));
    }
}