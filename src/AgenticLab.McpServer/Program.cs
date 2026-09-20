using AgenticLab.Extensibility.Examples;
using AgenticLab.Examples.Windfarm;
using AgenticLab.McpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery and resilience.
builder.AddServiceDefaults();
builder.Services.AddExample<WindfarmExample>(builder.Configuration, ExampleHost.Mcp);

// Explicit tool registration keeps optional example capabilities absent when disabled.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<TimeTools>()
    .AddExampleTools(builder.Services);

var app = builder.Build();

app.MapDefaultEndpoints();

// Streamable-HTTP MCP endpoint; clients connect here to discover and call tools.
app.MapMcp();

app.Run();
