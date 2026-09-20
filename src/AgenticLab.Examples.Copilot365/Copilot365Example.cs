using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Copilot365.Agents;
using AgenticLab.Examples.Copilot365.Tools;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticLab.Examples.Copilot365;

/// <summary>Opt-in workplace Copilot agents grounded in synthetic Microsoft 365 content.</summary>
public sealed class Copilot365Example : IAiServiceExample
{
    /// <summary>The module's configuration and catalogue identity.</summary>
    public const string Id = "copilot365";

    /// <summary>The existing API host key, retained for saved selections and chat requests.</summary>
    public const string HostKey = "microsoft365";

    /// <inheritdoc />
    public ExampleManifest Manifest { get; } = new(Id, "Copilot 365", [HostKey],
        [Microsoft365Agent.AgentName, M365ResearcherAgent.AgentName, M365AnalystAgent.AgentName])
    {
        ToolRisks = new Dictionary<string, ExampleToolRisk>
        {
            [nameof(Microsoft365Tool.SendMail)] = new("Medium",
                "Medium risk: models sending on your behalf; this sample validates inputs but never sends real email."),
        },
        Resources =
        [
            new("microsoft365", "Microsoft 365", "Microsoft Graph / sample data",
                [nameof(Microsoft365Tool.SearchEmail), nameof(Microsoft365Tool.SearchFiles),
                    nameof(Microsoft365Tool.SearchChats), nameof(Microsoft365Tool.GetCalendar),
                    nameof(Microsoft365Tool.FindPeople), nameof(Microsoft365Tool.SummarizeDocument),
                    nameof(Microsoft365Tool.SendMail)]),
        ],
    };

    /// <inheritdoc />
    public void AddAiServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<Microsoft365Tool>();
        services.AddSingleton<IAgentDefinition, Microsoft365Agent>();
        services.AddSingleton<IAgentDefinition, M365ResearcherAgent>();
        services.AddSingleton<IAgentDefinition, M365AnalystAgent>();
        services.AddSingleton<IVendorHarness, Microsoft365Harness>();
    }

    /// <summary>Uses the host's existing chat and catalogue routes; adds no module-specific API.</summary>
    public void MapApi(RouteGroupBuilder group) { }
}