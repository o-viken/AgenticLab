var builder = DistributedApplication.CreateBuilder(args);

// AI service: hosts the agent and the Wikipedia tool, exposed over HTTP.
var aiService = builder.AddProject<Projects.TheSeries_AiService>("aiservice")
    .WithHttpHealthCheck("/health");

// Interactive console: talks to the AI service via service discovery.
// Started explicitly so it gets an attached terminal for stdin.
builder.AddProject<Projects.TheSeries_Console>("console")
    .WithReference(aiService)
    .WaitFor(aiService)
    .WithExplicitStart();

builder.Build().Run();
