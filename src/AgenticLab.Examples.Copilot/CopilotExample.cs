using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Copilot.Agents;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticLab.Examples.Copilot;

/// <summary>Opt-in GitHub Copilot-style host reusing the core workspace agents.</summary>
public sealed class CopilotExample : IAiServiceExample
{
    /// <summary>The configuration and catalogue identity.</summary>
    public const string Id = "copilot";
    /// <summary>The stable API host key.</summary>
    public const string HostKey = "copilot";

    /// <inheritdoc />
    public ExampleManifest Manifest { get; } = new(Id, "GitHub Copilot", [HostKey], [])
    {
        HostPresentation = new Dictionary<string, ExampleHostPresentation>
        {
            [HostKey] = new("_content/AgenticLab.Examples.Copilot/host.svg", 30, "github-copilot")
            {
                LegacyKeys = ["Copilot"],
            },
        },
    };

    /// <inheritdoc />
    public void AddAiServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IVendorHarness, CopilotHarness>();

    /// <summary>Uses the existing chat routes without a custom panel or API.</summary>
    public void MapApi(RouteGroupBuilder group) { }
}