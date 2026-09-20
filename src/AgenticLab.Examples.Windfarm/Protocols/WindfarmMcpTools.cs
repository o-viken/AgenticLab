using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AgenticLab.Examples.Windfarm.Protocols;

/// <summary>Read-only MCP adapters calling the module's actual operational API over HTTP.</summary>
[McpServerToolType]
public sealed class WindfarmMcpTools(IHttpClientFactory clients)
{
    /// <summary>The exact names offered to the coordinator, excluding tools from other modules.</summary>
    public static IReadOnlyList<string> Names { get; } = ["WindfarmTelemetry", "WindfarmMaintenanceHistory", "WindfarmWeather", "WindfarmResources", "WindfarmProcedure"];

    /// <summary>Reads the condition-monitoring snapshot from the operational API.</summary>
    [McpServerTool(Name = "WindfarmTelemetry"), Description("Read synthetic wind-farm turbine telemetry, its units, timestamp and source ID. The scenario ID comes from WindfarmGetCase.")]
    public Task<string> Telemetry([Description("The active case's scenario ID.")] string scenarioId, CancellationToken cancellationToken) => Read(scenarioId, "telemetry", cancellationToken);

    /// <summary>Reads historical findings without inferring a confirmed fault.</summary>
    [McpServerTool(Name = "WindfarmMaintenanceHistory"), Description("Read the synthetic turbine's maintenance history with source references.")]
    public Task<string> History([Description("The active case's scenario ID.")] string scenarioId, CancellationToken cancellationToken) => Read(scenarioId, "history", cancellationToken);

    /// <summary>Reads fixture-time weather windows and illustrative output estimates.</summary>
    [McpServerTool(Name = "WindfarmWeather"), Description("Read synthetic maintenance weather windows and expected output in MW; these are scenario-time forecasts, not live weather.")]
    public Task<string> Weather([Description("The active case's scenario ID.")] string scenarioId, CancellationToken cancellationToken) => Read(scenarioId, "weather", cancellationToken);

    /// <summary>Reads available crew qualifications and inspection parts.</summary>
    [McpServerTool(Name = "WindfarmResources"), Description("Read synthetic crew qualifications, availability and parts inventory. No reservation or dispatch is performed.")]
    public Task<string> Resources([Description("The active case's scenario ID.")] string scenarioId, CancellationToken cancellationToken) => Read(scenarioId, "resources", cancellationToken);

    /// <summary>Reads illustrative planning constraints, never an equipment-control procedure.</summary>
    [McpServerTool(Name = "WindfarmProcedure"), Description("Read the illustrative inspection procedure and its planning constraints. This is training data, not certified operational guidance.")]
    public Task<string> Procedure([Description("The active case's scenario ID.")] string scenarioId, CancellationToken cancellationToken) => Read(scenarioId, "procedure", cancellationToken);

    private async Task<string> Read(string scenarioId, string resource, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scenarioId) || scenarioId.Length > 60) throw new ArgumentException("A bounded scenario ID is required.");
        using var http = clients.CreateClient("windfarm-evidence");
        using var response = await http.GetAsync($"{WindfarmExample.ApiPath}/scenarios/{Uri.EscapeDataString(scenarioId)}/{resource}", cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return content.Length <= 48_000 ? content : throw new InvalidOperationException("Operational evidence exceeded the result limit.");
    }
}