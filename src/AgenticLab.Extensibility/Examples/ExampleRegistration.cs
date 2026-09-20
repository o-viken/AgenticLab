using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgenticLab.Extensibility.Examples;

/// <summary>An immutable catalogue of explicitly enabled modules for this executable.</summary>
public sealed class ExampleCatalog(IEnumerable<IExampleModule> modules)
{
    /// <summary>The enabled local modules; component types never come from a remote response.</summary>
    public IReadOnlyList<IExampleModule> Modules { get; } = modules.ToArray();

    /// <summary>The public manifest owning the named agent, if any.</summary>
    public ExampleManifest? ForAgent(string name) => Modules.Select(module => module.Manifest)
        .FirstOrDefault(manifest => manifest.AgentNames.Contains(name, StringComparer.OrdinalIgnoreCase));
}

/// <summary>Explicit, default-off composition hooks shared by all executable roles.</summary>
public static class ExampleRegistration
{
    /// <summary>Loads one role's contributions only when Examples:{id}:Enabled is true.</summary>
    public static IServiceCollection AddExample<T>(this IServiceCollection services,
        IConfiguration configuration, ExampleHost host) where T : class, IExampleModule, new()
    {
        services.TryAddSingleton<ExampleCatalog>();
        var module = new T();
        var manifest = module.Manifest;
        if (!Regex.IsMatch(manifest.Id, "^[a-z][a-z0-9-]{1,39}$"))
            throw new InvalidOperationException("Example IDs must be lowercase route-safe names.");
        if (!configuration.GetValue<bool>($"Examples:{manifest.Id}:Enabled")) return services;

        foreach (var existing in services.Where(descriptor => descriptor.ServiceType == typeof(IExampleModule))
                     .Select(descriptor => ((IExampleModule)descriptor.ImplementationInstance!).Manifest))
        {
            if (existing.Id == manifest.Id || Overlap(existing.HostKeys, manifest.HostKeys)
                || Overlap(existing.AgentNames, manifest.AgentNames) || Overlap(existing.McpToolNames, manifest.McpToolNames)
                || Overlap(existing.RemoteAgentNames, manifest.RemoteAgentNames))
                throw new InvalidOperationException($"Example '{manifest.Id}' conflicts with '{existing.Id}'.");
        }

        services.AddSingleton<IExampleModule>(module);
        switch (host)
        {
            case ExampleHost.AiService when module is IAiServiceExample backend:
                backend.AddAiServices(services, configuration);
                break;
            case ExampleHost.Mcp when module is IMcpExample mcp:
                mcp.AddMcpServices(services, configuration);
                break;
            case ExampleHost.Web when module is IWebExample web:
                if (!typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(web.PanelType))
                    throw new InvalidOperationException("An example panel must implement IComponent.");
                web.AddWebServices(services, configuration);
                break;
        }
        return services;
    }

    /// <summary>Maps enabled backend modules and a credentials-free public catalogue.</summary>
    public static IEndpointRouteBuilder MapExamples(this IEndpointRouteBuilder endpoints)
    {
        var catalog = endpoints.ServiceProvider.GetRequiredService<ExampleCatalog>();
        endpoints.MapGet("/examples", () => TypedResults.Ok(catalog.Modules.Select(module => module.Manifest).ToArray()));
        foreach (var module in catalog.Modules.OfType<IAiServiceExample>())
            module.MapApi(endpoints.MapGroup($"/examples/{module.Manifest.Id}/api").WithTags(module.Manifest.DisplayName));
        return endpoints;
    }

    /// <summary>Adds enabled MCP tools without resolving their clients or calling another host.</summary>
    public static IMcpServerBuilder AddExampleTools(this IMcpServerBuilder builder, IServiceCollection services)
    {
        foreach (var module in services.Where(descriptor => descriptor.ServiceType == typeof(IExampleModule))
                     .Select(descriptor => descriptor.ImplementationInstance).OfType<IMcpExample>().ToArray())
            module.AddMcpTools(builder);
        return builder;
    }

    private static bool Overlap(IEnumerable<string> first, IEnumerable<string> second) =>
        first.Intersect(second, StringComparer.OrdinalIgnoreCase).Any();
}