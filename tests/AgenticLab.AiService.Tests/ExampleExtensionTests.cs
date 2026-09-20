using System.Text.Json;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Extensibility.Runtime;
using AgenticLab.AiService.Application.Conversations;
using AgenticLab.AiService.Application.Flow;
using AgenticLab.AiService.Application.Agents;
using AgenticLab.AiService.Demo.Tools;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class ExampleExtensionTests
{
    [Fact]
    public void HostToolsKeepExactSelectionOrderAndOriginalSchemas()
    {
        using var http = new HttpClient();
        var wiki = new WikiTool(http);
        var calculator = new CalculatorTool();
        var source = new DemoToolSource(wiki, calculator);
        var selected = source.GetTools(["Calculate", "GetWikiPage", "SearchWiki"]);
        Assert.Equal(new[] { "Calculate", "GetWikiPage", "SearchWiki" }, selected.Select(tool => tool.Name));
        var originals = wiki.AsTools().Concat(calculator.AsTools()).OfType<AIFunction>()
            .ToDictionary(tool => tool.Name);
        foreach (var tool in selected.Cast<AIFunction>())
        {
            Assert.Equal(originals[tool.Name].Description, tool.Description);
            Assert.Equal(originals[tool.Name].JsonSchema.GetRawText(), tool.JsonSchema.GetRawText());
        }
        Assert.Single(source.GetTools(["Calculate"]));
        Assert.Empty(source.GetTools([]));
    }

    [Theory]
    [InlineData("calculate")]
    [InlineData("WriteFile")]
    [InlineData("GetCurrentTime")]
    [InlineData("DelegateToAgent")]
    public void HostToolsRejectUnpublishedNames(string name)
    {
        using var http = new HttpClient();
        var source = new DemoToolSource(new WikiTool(http), new CalculatorTool());
        Assert.Throws<ArgumentException>(() => source.GetTools(["Calculate", name]));
    }

    [Fact]
    public async Task HostToolsInvokeExistingCalculatorAndWikipediaImplementations()
    {
        using var handler = new WikipediaHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://wikipedia.invalid") };
        var source = new DemoToolSource(new WikiTool(http), new CalculatorTool());
        var calculate = Assert.IsAssignableFrom<AIFunction>(Assert.Single(source.GetTools(["Calculate"])));
        var calculation = await calculate.InvokeAsync(new AIFunctionArguments { ["expression"] = "(6 * 2) + 3" });
        Assert.Equal("15", Assert.IsType<JsonElement>(calculation).GetString());
        Assert.Equal(0, handler.RequestCount);
        var search = Assert.IsAssignableFrom<AIFunction>(Assert.Single(source.GetTools(["SearchWiki"])));
        var result = await search.InvokeAsync(new AIFunctionArguments { ["query"] = "Ada Lovelace" });
        Assert.Contains("Ada Lovelace: Mathematician", Assert.IsType<JsonElement>(result).GetString());
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("/w/rest.php/v1/search/page", handler.RequestUri!.AbsolutePath);
        Assert.Contains("q=Ada%20Lovelace", handler.RequestUri.Query);
    }

    [Fact]
    public async Task ArbitraryModuleRegistersOnlyEnabledRoleAndOwnsItsRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddExample<ParcelExample>(Configuration(true), ExampleHost.AiService);
        await using var app = builder.Build();
        app.MapExamples();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
        Assert.Equal("parcel-data", await client.GetStringAsync("/examples/parcels/api/status"));
        Assert.Contains("parcels", await client.GetStringAsync("/examples"));
        Assert.Single(app.Services.GetRequiredService<ExampleCatalog>().Modules);
        await app.StopAsync();

        var disabled = new ServiceCollection().AddExample<ParcelExample>(Configuration(false), ExampleHost.AiService);
        using var provider = disabled.BuildServiceProvider();
        Assert.Empty(provider.GetRequiredService<ExampleCatalog>().Modules);
        Assert.Null(provider.GetService<ParcelState>());
        var otherRole = new ServiceCollection().AddExample<ParcelExample>(Configuration(true), ExampleHost.Mcp);
        using var otherProvider = otherRole.BuildServiceProvider();
        Assert.Null(otherProvider.GetService<ParcelState>());
    }

    [Fact]
    public void DuplicateRegistrationsAreRejected()
    {
        var services = new ServiceCollection().AddExample<ParcelExample>(Configuration(true), ExampleHost.AiService);
        Assert.Throws<InvalidOperationException>(() => services.AddExample<ParcelExample>(Configuration(true), ExampleHost.AiService));
    }

    [Fact]
    public async Task RunIdentitySurvivesReactivationAndConcurrentRunsStayIsolated()
    {
        var accessor = new AgentRunContext();
        async Task<string> Run(string conversation)
        {
            using var scopes = RunScopeSet.Begin(null, null, null, null, false, conversation, "test-agent");
            await Task.Yield();
            scopes.Activate();
            Assert.Equal("test-agent", accessor.Current?.AgentName);
            return accessor.Current!.ConversationId;
        }
        Assert.Equal(new[] { "first", "second" }, await Task.WhenAll(Run("first"), Run("second")));
        Assert.Null(accessor.Current);
    }

    private static IConfiguration Configuration(bool enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Examples:parcels:Enabled"] = enabled.ToString() }).Build();

    [Fact]
    public void RediscoveryUpdatesAdvertisedToolsAlongsideExecutableAgent()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://unused.invalid", ["AzureOpenAI:Deployment"] = "test",
            ["AzureOpenAI:ApiKey"] = "not-used-by-this-test",
        }).Build();
        var definition = new DiscoveredDefinition();
        var catalog = new AgentCatalog(new ChatClientProvider(configuration), [definition]);
        Assert.Empty(Assert.Single(catalog.Agents).Tools);
        Assert.True(catalog.TryResolve(definition.Name, out var before, out _));
        definition.CurrentTools = [AIFunctionFactory.Create(() => "result", "ParcelRead")];
        catalog.RefreshDiscoveryAgents();
        Assert.Equal("ParcelRead", Assert.Single(Assert.Single(catalog.Agents).Tools));
        Assert.True(catalog.TryResolve(definition.Name, out var after, out _));
        Assert.NotSame(before, after);
        definition.CurrentTools = [];
        catalog.RefreshDiscoveryAgents();
        Assert.Empty(Assert.Single(catalog.Agents).Tools);
    }

    private sealed class WikipediaHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"pages":[{"title":"Ada Lovelace","description":"Mathematician"}]}"""),
            });
        }
    }

    private sealed class DiscoveredDefinition : AgentDefinitionBase
    {
        public override string Name => "Discovered";
        public override string Description => "Test discovery";
        protected override string Persona => "Test";
        public override bool SupportsMcp => true;
        public IList<AITool> CurrentTools { get; set; } = [];
        public override IList<AITool> Tools => CurrentTools;
    }

    public sealed class ParcelState;

    public sealed class ParcelExample : IAiServiceExample
    {
        public ExampleManifest Manifest => new("parcels", "Parcels", ["parcel-host"], ["ParcelAgent"]);
        public void AddAiServices(IServiceCollection services, IConfiguration configuration) => services.AddSingleton<ParcelState>();
        public void MapApi(RouteGroupBuilder group) => group.MapGet("/status", () => Results.Text("parcel-data"));
    }
}