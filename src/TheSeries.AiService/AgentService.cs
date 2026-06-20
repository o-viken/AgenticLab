using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using TheSeries.AiService.Application;

namespace TheSeries.AiService;

/// <summary>
/// Builds the shared chat client backed by Azure OpenAI used by every agent.
/// </summary>
public static class AgentService
{
    /// <summary>
    /// Creates the shared <see cref="IChatClient"/> backed by Azure OpenAI, with function invocation and
    /// OpenTelemetry enabled. The same client is reused by every agent in the catalog.
    /// </summary>
    /// <param name="configuration">Configuration providing the <c>AzureOpenAI:Endpoint</c>, <c>AzureOpenAI:Deployment</c> and <c>AzureOpenAI:ApiKey</c> values.</param>
    /// <returns>A configured <see cref="IChatClient"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a required Azure OpenAI configuration value is missing.</exception>
    public static IChatClient CreateChatClient(IConfiguration configuration)
    {
        var endpoint = Required(configuration, "AzureOpenAI:Endpoint");
        var deployment = Required(configuration, "AzureOpenAI:Deployment");
        var apiKey = Required(configuration, "AzureOpenAI:ApiKey");

        return new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
            .GetChatClient(deployment)
            .AsIChatClient()
            .AsBuilder()
            // Outermost: strip the tools the caller disabled for this run before function invocation or
            // the model ever see them. The agent framework only unions per-run tools, so restricting to a
            // subset of an agent's tools must happen here rather than through run options.
            .Use(inner => new ToolFilteringChatClient(inner))
            .UseFunctionInvocation()
            .Use(inner => new CapturingChatClient(inner))
            .UseOpenTelemetry()
            .Build();
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException(
            $"Missing configuration '{key}'. Set it in appsettings or user-secrets.");
}

