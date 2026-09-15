using System.ClientModel;
using System.Collections.Concurrent;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application;

/// <summary>
/// Builds and caches one <see cref="IChatClient"/> per Azure OpenAI deployment so different agents can
/// run on different models. Every client shares the same endpoint and credential and is wrapped in the
/// same pipeline (tool filtering, function invocation, request/response capture and OpenTelemetry); only
/// the deployment differs. Clients are created lazily and reused, since the deployment is baked into the
/// underlying Azure client at construction and cannot be overridden per call.
/// </summary>
public sealed class ChatClientProvider
{
    private readonly IConfiguration _configuration;
    private readonly Uri _endpoint;
    private readonly ApiKeyCredential _credential;
    private readonly string _defaultDeployment;
    private readonly bool _forceDefaultModel;
    private readonly ConcurrentDictionary<string, IChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the shared Azure OpenAI connection settings and the default deployment.
    /// </summary>
    /// <param name="configuration">Configuration providing the <c>AzureOpenAI:Endpoint</c>, <c>AzureOpenAI:Deployment</c> and <c>AzureOpenAI:ApiKey</c> values, plus optional per-agent <c>Agents:{Name}:Deployment</c> overrides and the <c>AzureOpenAI:ForceDefaultModel</c> flag that runs every agent on the default deployment while still showing its declared model.</param>
    /// <exception cref="InvalidOperationException">Thrown when a required Azure OpenAI configuration value is missing.</exception>
    public ChatClientProvider(IConfiguration configuration)
    {
        _configuration = configuration;
        _endpoint = new Uri(Required(configuration, "AzureOpenAI:Endpoint"));
        _credential = new ApiKeyCredential(Required(configuration, "AzureOpenAI:ApiKey"));
        _defaultDeployment = Required(configuration, "AzureOpenAI:Deployment");
        _forceDefaultModel = configuration.GetValue<bool>("AzureOpenAI:ForceDefaultModel");
    }

    /// <summary>The deployment used when an agent does not select its own.</summary>
    public string DefaultDeployment => _defaultDeployment;

    /// <summary>
    /// Returns the pipeline-wrapped chat client for the given deployment, creating and caching it on first
    /// use. A null or blank deployment resolves to the <see cref="DefaultDeployment"/>.
    /// </summary>
    /// <param name="deployment">The Azure OpenAI deployment name, or null/blank for the default.</param>
    /// <returns>The shared <see cref="IChatClient"/> for that deployment.</returns>
    public IChatClient Get(string? deployment)
    {
        var name = string.IsNullOrWhiteSpace(deployment) ? _defaultDeployment : deployment.Trim();
        return _clients.GetOrAdd(name, Build);
    }

    /// <summary>
    /// Resolves the deployment an agent should run on. Precedence: the <c>Agents:{Name}:Deployment</c>
    /// configuration value, then the agent's own <see cref="IAgentDefinition.ModelId"/> default, then the
    /// global <see cref="DefaultDeployment"/>.
    /// </summary>
    /// <param name="definition">The agent whose deployment to resolve.</param>
    /// <returns>The effective deployment name for the agent.</returns>
    public string ResolveDeployment(IAgentDefinition definition)
    {
        var configured = _configuration[$"Agents:{definition.Name}:Deployment"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return string.IsNullOrWhiteSpace(definition.ModelId) ? _defaultDeployment : definition.ModelId!.Trim();
    }

    /// <summary>
    /// Resolves the deployment an agent actually <em>executes</em> on, which is normally the same as its
    /// declared deployment from <see cref="ResolveDeployment"/>. When <c>AzureOpenAI:ForceDefaultModel</c>
    /// is set, every agent runs on the <see cref="DefaultDeployment"/> instead — so the declared model is
    /// surfaced for display only (e.g. on the LLM node) while a single real model answers. This keeps the
    /// per-agent model picture purely visual until real per-deployment routing is turned back on.
    /// </summary>
    /// <param name="declaredDeployment">The agent's declared (display) deployment from <see cref="ResolveDeployment"/>.</param>
    /// <returns>The deployment the agent's chat client is built on.</returns>
    public string ExecutionDeployment(string declaredDeployment) =>
        _forceDefaultModel || string.IsNullOrWhiteSpace(declaredDeployment)
            ? _defaultDeployment
            : declaredDeployment.Trim();

    private IChatClient Build(string deployment) =>
        new AzureOpenAIClient(_endpoint, _credential)
            .GetChatClient(deployment)
            .AsIChatClient()
            .AsBuilder()
            // Outermost: strip the tools the caller disabled for this run before function invocation or
            // the model ever see them. The agent framework only unions per-run tools, so restricting to a
            // subset of an agent's tools must happen here rather than through run options.
            .Use(inner => new ToolFilteringChatClient(inner))
            .UseFunctionInvocation(configure: client => client.FunctionInvoker = FlowExecutionScope.InvokeFunctionAsync)
            .Use(inner => new CapturingChatClient(inner))
            .UseOpenTelemetry()
            .Build();

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] ?? throw new InvalidOperationException(
            $"Missing configuration '{key}'. Set it in appsettings or user-secrets.");
}
