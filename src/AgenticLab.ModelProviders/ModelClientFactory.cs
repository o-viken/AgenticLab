using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace AgenticLab.ModelProviders;

/// <summary>Creates raw model clients; each consuming host remains responsible for its own execution pipeline.</summary>
public sealed class ModelClientFactory
{
    private readonly ModelConnectionOptions _connection;
    private readonly PipelineTransport? _transport;

    /// <summary>Uses validated startup settings without contacting the provider until a model request is made.</summary>
    public ModelClientFactory(ModelConnectionOptions connection) : this(connection, null) { }

    internal ModelClientFactory(ModelConnectionOptions connection, PipelineTransport? transport)
    {
        _connection = connection;
        _transport = transport;
    }

    /// <summary>Creates a client for an explicit model or deployment, falling back to the configured default.</summary>
    public IChatClient Create(string? model = null)
    {
        var modelId = string.IsNullOrWhiteSpace(model) ? _connection.DefaultModel : model.Trim();
        if (_connection.Provider == ModelProvider.AzureOpenAI)
        {
            var options = new AzureOpenAIClientOptions();
            if (_transport is not null) options.Transport = _transport;
            return new AzureOpenAIClient(_connection.Endpoint, _connection.Credential, options)
                .GetChatClient(modelId).AsIChatClient();
        }

        var nativeOptions = new OpenAIClientOptions { Endpoint = _connection.Endpoint };
        if (_transport is not null) nativeOptions.Transport = _transport;
        var client = new OpenAIClient(_connection.Credential, nativeOptions)
            .GetChatClient(modelId).AsIChatClient();
        return _connection.Provider == ModelProvider.Gemini ? new GeminiChatClient(client) : client;
    }
}