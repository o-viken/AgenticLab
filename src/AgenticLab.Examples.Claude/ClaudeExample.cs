using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Claude.Agents;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticLab.Examples.Claude;

/// <summary>Opt-in Claude-style host reusing the core tool-free conversational agent.</summary>
public sealed class ClaudeExample : IAiServiceExample
{
    /// <summary>The configuration and catalogue identity.</summary>
    public const string Id = "claude";
    /// <summary>The stable API host key.</summary>
    public const string HostKey = "claude";

    /// <inheritdoc />
    public ExampleManifest Manifest { get; } = new(Id, "Claude", [HostKey], [])
    {
        HostPresentation = new Dictionary<string, ExampleHostPresentation>
        {
            [HostKey] = new("_content/AgenticLab.Examples.Claude/host.svg", 50, "claude")
            {
                LegacyKeys = ["Claude"],
            },
        },
    };

    /// <inheritdoc />
    public void AddAiServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IVendorHarness, ClaudeHarness>();

    /// <summary>Uses the existing chat routes without a custom panel or API.</summary>
    public void MapApi(RouteGroupBuilder group) { }
}