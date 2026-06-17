var builder = DistributedApplication.CreateBuilder(args);

// Azure OpenAI settings live in the AppHost user-secrets and are injected into the AI service.
var aiService = builder.AddProject<Projects.TheSeries_AiService>("aiservice")
    .WithHttpHealthCheck("/health")
    .WithEnvironment("AzureOpenAI__Endpoint", builder.Configuration["AzureOpenAI:Endpoint"])
    .WithEnvironment("AzureOpenAI__Deployment", builder.Configuration["AzureOpenAI:Deployment"])
    .WithEnvironment("AzureOpenAI__ApiKey", builder.Configuration["AzureOpenAI:ApiKey"]);

// Interactive console: talks to the AI service via service discovery.
// Started explicitly so it gets an attached terminal for stdin.
builder.AddProject<Projects.TheSeries_Console>("console")
    .WithReference(aiService)
    .WaitFor(aiService)
    .WithExplicitStart();

builder.Build().Run();
