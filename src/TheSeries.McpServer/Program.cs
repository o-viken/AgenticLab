var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery and resilience.
builder.AddServiceDefaults();

// A minimal Model Context Protocol server exposed over HTTP. Tools are discovered from the
// assembly via the [McpServerTool] attribute (see Tools/TimeTools.cs).
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapDefaultEndpoints();

// Streamable-HTTP MCP endpoint; clients connect here to discover and call tools.
app.MapMcp();

app.Run();
