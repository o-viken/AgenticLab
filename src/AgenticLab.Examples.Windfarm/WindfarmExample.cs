using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Windfarm.Agents;
using AgenticLab.Examples.Windfarm.Api;
using AgenticLab.Examples.Windfarm.Components;
using AgenticLab.Examples.Windfarm.Process;
using AgenticLab.Examples.Windfarm.Protocols;
using AgenticLab.Examples.Windfarm.Tools;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgenticLab.Examples.Windfarm;

/// <summary>All Windfarm contributions, explicitly loaded per host role and disabled by default.</summary>
public sealed class WindfarmExample : IAiServiceExample, IMcpExample, IA2AExample, IWebExample
{
    /// <summary>The stable configuration, route and host key.</summary>
    public const string Id = "windfarm";
    /// <summary>The example-owned operational API prefix.</summary>
    public const string ApiPath = "/examples/windfarm/api";
    /// <inheritdoc />
    public ExampleManifest Manifest { get; } = new(Id, "Windfarm Operations", [Id], [WindfarmCoordinator.AgentName], RequiresUi: true)
    {
        McpToolNames = WindfarmMcpTools.Names,
        RemoteAgentNames = WindfarmProcess.Reviewers,
        ToolRisks = new Dictionary<string, ExampleToolRisk>
        {
            ["WindfarmDraftPlan"] = new("Medium", "Medium risk: changes an isolated draft and invalidates its review receipts; cannot approve an order."),
            ["WindfarmSubmitProposal"] = new("Medium", "Medium risk: freezes a reviewed proposal; does not create or approve a work order."),
            ["DelegateToAgent"] = new("Medium", "Medium risk: sends synthetic evidence to an allowlisted specialist and records a revision-bound review."),
        },
        Resources =
        [
            new("windfarm-mcp", "Windfarm evidence", "MCP / HTTP / operational API", WindfarmMcpTools.Names.ToArray()),
            new("windfarm-process", "Maintenance case", "Host-enforced synthetic process", ["WindfarmGetCase", "WindfarmEvaluateOptions", "WindfarmDraftPlan", "WindfarmSubmitProposal"]),
        ],
    };

    /// <inheritdoc />
    public IReadOnlyList<RemoteAgentDefinition> Specialists => WindfarmSpecialists.All;

    /// <inheritdoc />
    public Type PanelType => typeof(WindfarmPanel);

    /// <inheritdoc />
    public void AddWebServices(IServiceCollection services, IConfiguration configuration)
    {
#pragma warning disable EXTEXP0001
        services.AddHttpClient<IWindfarmClient, WindfarmClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["AiService:Url"] ?? "https+http://aiservice");
            client.Timeout = TimeSpan.FromSeconds(20);
        }).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }

    /// <inheritdoc />
    public void AddAiServices(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<WindfarmOptions>().Bind(configuration.GetSection($"Examples:{Id}"))
            .Validate(options => options.MaxCases is > 0 and <= 1000 && options.InactiveTtl > TimeSpan.Zero
                && options.CleanupInterval > TimeSpan.Zero, "Windfarm retention limits must be positive and capacity at most 1000.")
            .ValidateOnStart();
        services.AddSingleton<IWindfarmProcess, WindfarmProcess>();
        services.AddHostedService<WindfarmRetention>();
        services.AddSingleton<WindfarmTools>();
        services.AddSingleton<IAgentDefinition, WindfarmCoordinator>();
        services.AddSingleton<IVendorHarness, WindfarmHarness>();
        services.AddOpenApi(Id, options => options.ShouldInclude = description => description.GroupName == Id);
    }

    /// <inheritdoc />
    public void MapApi(RouteGroupBuilder group) => WindfarmEndpoints.Map(group);

    /// <inheritdoc />
    public void AddMcpServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddHttpClient("windfarm-evidence", client =>
        {
            client.BaseAddress = new Uri(configuration["AiService:Url"] ?? "https+http://aiservice");
            client.Timeout = TimeSpan.FromSeconds(20);
        });

    /// <inheritdoc />
    public void AddMcpTools(IMcpServerBuilder builder) => builder.WithTools<WindfarmMcpTools>();
}