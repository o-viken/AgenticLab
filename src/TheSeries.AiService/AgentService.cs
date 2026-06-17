using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService;

/// <summary>
/// Builds the chat agent backed by Azure OpenAI and equipped with the Wikipedia tool.
/// </summary>
public static class AgentService
{
    private const string Instructions =
        "You are WikiAssistant, a concise and friendly research helper. " +
        "When a question asks about facts, people, places, or concepts, use the SearchWiki tool to find " +
        "relevant pages and the GetWikiPage tool to read a page summary before answering. " +
        "Always base factual answers on what the tools return and mention the page title you used.";

    /// <summary>
    /// Creates the stateless WikiAssistant agent backed by Azure OpenAI and equipped with the Wikipedia tool.
    /// </summary>
    /// <param name="configuration">Configuration providing the <c>AzureOpenAI:Endpoint</c>, <c>AzureOpenAI:Deployment</c> and <c>AzureOpenAI:ApiKey</c> values.</param>
    /// <param name="wikiTool">The Wikipedia tool whose methods are exposed to the agent.</param>
    /// <returns>A configured <see cref="AIAgent"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a required Azure OpenAI configuration value is missing.</exception>
    public static AIAgent CreateWikiAgent(IConfiguration configuration, WikiTool wikiTool)
    {
        var endpoint = Required(configuration, "AzureOpenAI:Endpoint");
        var deployment = Required(configuration, "AzureOpenAI:Deployment");
        var apiKey = Required(configuration, "AzureOpenAI:ApiKey");

        IChatClient chatClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
            .GetChatClient(deployment)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry()
            .Build();

        return new ChatClientAgent(
            chatClient,
            instructions: Instructions,
            name: "WikiAssistant",
            tools: wikiTool.AsTools());
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException(
            $"Missing configuration '{key}'. Set it in appsettings or user-secrets.");
}

