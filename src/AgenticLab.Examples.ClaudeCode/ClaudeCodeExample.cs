using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.ClaudeCode.Agents;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticLab.Examples.ClaudeCode;

/// <summary>Opt-in Claude Code-style host reusing the core planning and coding agents.</summary>
public sealed class ClaudeCodeExample : IAiServiceExample
{
    /// <summary>The configuration and catalogue identity.</summary>
    public const string Id = "claude-code";
    /// <summary>The stable API host key.</summary>
    public const string HostKey = "claude-code";

    /// <inheritdoc />
    public ExampleManifest Manifest { get; } = new(Id, "Claude Code", [HostKey], [])
    {
        HostPresentation = new Dictionary<string, ExampleHostPresentation>
        {
            [HostKey] = new("_content/AgenticLab.Examples.ClaudeCode/host.svg", 40, "claude-code")
            {
                LegacyKeys = ["ClaudeCode"],
            },
        },
    };

    /// <inheritdoc />
    public void AddAiServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IVendorHarness, ClaudeCodeHarness>();

    /// <summary>Uses the existing chat routes without a custom panel or API.</summary>
    public void MapApi(RouteGroupBuilder group) { }
}