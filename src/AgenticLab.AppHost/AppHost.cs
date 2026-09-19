using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// A tiny Model Context Protocol server exposing the local time, consumed by the AI service over MCP.
var timeMcp = builder.AddProject<Projects.AgenticLab_McpServer>("mcpserver")
    .WithHttpHealthCheck("/health");

// An Agent2Agent (A2A) server hosting a research sub-agent, called by the AI service's Orchestrator agent
// over the A2A protocol. It hosts an LLM agent, so it needs the same Azure OpenAI settings as the AI
// service (injected from the AppHost user-secrets).
var a2aServer = builder.AddProject<Projects.AgenticLab_A2AServer>("a2aserver")
    .WithHttpHealthCheck("/health")
    .WithEnvironment("AzureOpenAI__Endpoint", builder.Configuration["AzureOpenAI:Endpoint"])
    .WithEnvironment("AzureOpenAI__Deployment", builder.Configuration["AzureOpenAI:Deployment"])
    .WithEnvironment("AzureOpenAI__ApiKey", builder.Configuration["AzureOpenAI:ApiKey"]);

// Azure OpenAI settings live in the AppHost user-secrets and are injected into the AI service.
var aiService = builder.AddProject<Projects.AgenticLab_AiService>("aiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(timeMcp)
    .WaitFor(timeMcp)
    .WithReference(a2aServer)
    .WaitFor(a2aServer)
    .WithEnvironment("AzureOpenAI__Endpoint", builder.Configuration["AzureOpenAI:Endpoint"])
    .WithEnvironment("AzureOpenAI__Deployment", builder.Configuration["AzureOpenAI:Deployment"])
    .WithEnvironment("AzureOpenAI__ApiKey", builder.Configuration["AzureOpenAI:ApiKey"]);

// Interactive console: talks to the AI service via service discovery.
// Started explicitly so it gets an attached terminal for stdin.
builder.AddProject<Projects.AgenticLab_Console>("console")
    .WithReference(aiService)
    .WaitFor(aiService)
    .WithExplicitStart();

// Blazor web UI that visualizes the live data flow through the agent. Reaches the AI service
// via service discovery and is exposed on an external HTTP endpoint.
builder.AddProject<Projects.AgenticLab_Web>("web")
    .WithReference(aiService)
    .WaitFor(aiService)
    .WithExternalHttpEndpoints();

if (builder.Configuration.GetValue<bool>("ReactFrontend:Enabled"))
{
    var bff = builder.AddProject<Projects.AgenticLab_Bff>("react-bff")
        .WithReference(aiService)
        .WaitFor(aiService)
        .WithHttpHealthCheck("/health")
        .WithExternalHttpEndpoints();

    var react = builder.AddViteApp("react", "../AgenticLab.React")
        .WithExternalHttpEndpoints();

    if (builder.ExecutionContext.IsRunMode)
    {
        react.WithEnvironment("BFF_URL", bff.GetEndpoint("http"))
            .WaitFor(bff);
    }

    bff.PublishWithContainerFiles(react, "wwwroot");
}

builder.Build().Run();
