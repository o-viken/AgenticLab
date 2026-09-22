using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Gemini.Agents;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticLab.Examples.Gemini;

/// <summary>Opt-in Gemini-style host reusing the core tool-free conversational agent.</summary>
public sealed class GeminiExample : IAiServiceExample
{
    /// <summary>The configuration and catalogue identity.</summary>
    public const string Id = "gemini";
    /// <summary>The stable API host key.</summary>
    public const string HostKey = "gemini";

    /// <inheritdoc />
    public ExampleManifest Manifest { get; } = new(Id, "Gemini", [HostKey], [])
    {
        HostPresentation = new Dictionary<string, ExampleHostPresentation>
        {
            [HostKey] = new("_content/AgenticLab.Examples.Gemini/host.svg", 20, "gemini")
            {
                LegacyKeys = ["Gemini"],
            },
        },
    };

    /// <inheritdoc />
    public void AddAiServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IVendorHarness, GeminiHarness>();

    /// <summary>Uses the existing chat routes without a custom panel or API.</summary>
    public void MapApi(RouteGroupBuilder group) { }
}