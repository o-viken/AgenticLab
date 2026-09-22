using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.ChatGpt.Agents;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticLab.Examples.ChatGpt;

/// <summary>Opt-in ChatGPT-style host with bounded Wikipedia and calculator tools.</summary>
public sealed class ChatGptExample : IAiServiceExample
{
    /// <summary>The configuration and catalogue identity.</summary>
    public const string Id = "chatgpt";
    /// <summary>The stable API host key.</summary>
    public const string HostKey = "chatgpt";

    /// <inheritdoc />
    public ExampleManifest Manifest { get; } = new(Id, "ChatGPT", [HostKey], [ChatGptAgent.AgentName])
    {
        HostPresentation = new Dictionary<string, ExampleHostPresentation>
        {
            [HostKey] = new("_content/AgenticLab.Examples.ChatGpt/host.svg", 10, "chatgpt")
            {
                LegacyKeys = ["ChatGpt"],
            },
        },
    };

    /// <inheritdoc />
    public void AddAiServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAgentDefinition, ChatGptAgent>();
        services.AddSingleton<IVendorHarness, ChatGptHarness>();
    }

    /// <summary>Uses the existing chat and catalogue routes without a custom panel or API.</summary>
    public void MapApi(RouteGroupBuilder group) { }
}