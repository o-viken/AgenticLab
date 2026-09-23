using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using AgenticLab.AiService.Application.Agents;
using AgenticLab.ModelProviders;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class ModelProviderTests
{
    [Theory]
    [InlineData("OpenAI", "https://api.openai.com/v1/chat/completions")]
    [InlineData("Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions")]
    public async Task NativeProvidersUseTheirOwnEndpointKeyAndModelWithoutAzure(string provider, string endpoint)
    {
        var configuration = Configuration(provider);
        configuration["Models:Provider"] = $" {provider.ToLowerInvariant()} ";
        configuration["AzureOpenAI:Endpoint"] = "unused-invalid-endpoint";
        configuration["AzureOpenAI:ForceDefaultModel"] = "unused-invalid-flag";
        var connection = new ModelConnectionOptions(configuration);
        using var handler = new ResponseHandler(async request =>
        {
            Assert.Equal(endpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);
            Assert.False(request.Headers.Contains("api-key"));
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
            return JsonResponse("""
                {"id":"reply-1","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"Hello"},"finish_reason":"stop"}]}
                """);
        });
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(connection, new HttpClientPipelineTransport(httpClient)).Create();

        var response = await client.GetResponseAsync("Hi");

        Assert.Equal("Hello", response.Text);
        Assert.False(connection.ForceDefaultModel);
    }

    [Theory]
    [InlineData("OpenAI", false)]
    [InlineData("OpenAI", true)]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    public async Task ToolCallsSurviveStreamingAndConversationHistory(string provider, bool streaming)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var requests = 0;
        var invocations = new List<string>();
        using var handler = new ResponseHandler(async request =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(timeout.Token));
            var root = body.RootElement;
            Assert.Equal("EchoValue", root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
            if (requests++ == 0) return ToolResponse(streaming);

            var messages = root.GetProperty("messages").EnumerateArray().ToArray();
            var calls = messages.Single(message => message.TryGetProperty("tool_calls", out _)).GetProperty("tool_calls");
            Assert.Equal(2, calls.GetArrayLength());
            Assert.Equal(2, messages.Count(message => message.GetProperty("role").GetString() == "tool"));
            if (provider == "Gemini")
            {
                Assert.Equal("opaque-signature-1", calls[0].GetProperty("extra_content").GetProperty("google").GetProperty("thought_signature").GetString());
            }
            return FinalResponse(root.TryGetProperty("stream", out var stream) && stream.GetBoolean());
        });
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(new ModelConnectionOptions(Configuration(provider)),
            new HttpClientPipelineTransport(httpClient)).Create().AsBuilder().UseFunctionInvocation().Build();
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create((string value) => { invocations.Add(value); return value; }, name: "EchoValue")],
        };
        var history = new List<ChatMessage> { new(ChatRole.User, "Echo first and second.") };
        var response = streaming
            ? await client.GetStreamingResponseAsync(history, options, timeout.Token).ToChatResponseAsync(cancellationToken: timeout.Token)
            : await client.GetResponseAsync(history, options, timeout.Token);

        Assert.EndsWith("Done", response.Text);
        Assert.Equal(["first", "second"], invocations);
        history.AddRange(response.Messages);
        history.Add(new(ChatRole.User, "Continue."));
        history = JsonSerializer.Deserialize<List<ChatMessage>>(JsonSerializer.Serialize(history))!;
        var followUp = await client.GetResponseAsync(history, options, timeout.Token);
        Assert.EndsWith("Done", followUp.Text);
        Assert.Equal(3, requests);
    }

    [Fact]
    public void ExistingAzureConfigurationRemainsTheDefault()
    {
        var configuration = Configuration("AzureOpenAI");
        configuration["Models:Provider"] = null;
        configuration["AzureOpenAI:ForceDefaultModel"] = "true";

        var connection = new ModelConnectionOptions(configuration);

        Assert.Equal(ModelProvider.AzureOpenAI, connection.Provider);
        Assert.Equal("test-model", connection.DefaultModel);
        Assert.True(connection.ForceDefaultModel);
        configuration["Models:ForceDefaultModel"] = "false";
        Assert.False(new ModelConnectionOptions(configuration).ForceDefaultModel);
    }

    [Theory]
    [InlineData("OpenAI", "OpenAI:ApiKey")]
    [InlineData("OpenAI", "OpenAI:Model")]
    [InlineData("Gemini", "Gemini:ApiKey")]
    [InlineData("Gemini", "Gemini:Model")]
    [InlineData("AzureOpenAI", "AzureOpenAI:Endpoint")]
    public void MissingSelectedSettingsNameTheSettingWithoutExposingCredentials(string provider, string key)
    {
        var configuration = Configuration(provider);
        configuration[key] = " ";

        var error = Assert.Throws<InvalidOperationException>(() => new ModelConnectionOptions(configuration));

        Assert.Contains(key, error.Message);
        Assert.DoesNotContain("test-key", error.Message);
    }

    [Fact]
    public void UnknownProviderIsRejectedWithoutFallingBack()
    {
        var configuration = Configuration("OpenAI");
        configuration["Models:Provider"] = "unsupported";

        var error = Assert.Throws<InvalidOperationException>(() => new ModelConnectionOptions(configuration));

        Assert.Contains("Models:Provider", error.Message);
    }

    [Theory]
    [InlineData("AzureOpenAI", "azure-coder")]
    [InlineData("OpenAI", "declared-model")]
    [InlineData("Gemini", "declared-model")]
    public void AgentModelPrecedenceDoesNotLeakAzureDeploymentOverrides(string provider, string expected)
    {
        var configuration = Configuration(provider);
        configuration["Agents:Coder:Deployment"] = "azure-coder";
        var clients = new ChatClientProvider(configuration);
        var definition = new ModelDefinition("declared-model");

        Assert.Equal(expected, clients.ResolveDeployment(definition));
        configuration["Agents:Coder:Model"] = " selected-model ";
        Assert.Equal("selected-model", clients.ResolveDeployment(definition));
        configuration["Agents:Coder:Model"] = " ";
        configuration["Agents:Coder:Deployment"] = null;
        Assert.Equal("declared-model", clients.ResolveDeployment(definition));
        Assert.Equal("test-model", clients.ResolveDeployment(new ModelDefinition(null)));
    }

    [Theory]
    [InlineData("AzureOpenAI", "test-model")]
    [InlineData("OpenAI", "declared-model")]
    [InlineData("Gemini", "declared-model")]
    public void NativeProvidersDoNotInheritTheAzureForceDefaultFlag(string provider, string expected)
    {
        var configuration = Configuration(provider);
        configuration["AzureOpenAI:ForceDefaultModel"] = "true";
        Assert.Equal(expected, new ChatClientProvider(configuration).ExecutionDeployment("declared-model"));

        configuration["Models:ForceDefaultModel"] = "true";
        var clients = new ChatClientProvider(configuration);
        Assert.Equal("declared-model", clients.ResolveDeployment(new ModelDefinition("declared-model")));
        Assert.Equal("test-model", clients.ExecutionDeployment("declared-model"));
        Assert.Equal("test-model", clients.Get(clients.ExecutionDeployment("declared-model"))
            .GetService<ChatClientMetadata>()!.DefaultModelId);
    }

    [Theory]
    [InlineData("AzureOpenAI", true)]
    [InlineData("OpenAI", false)]
    [InlineData("Gemini", false)]
    public void ClientCachingRespectsProviderModelIdentifierSemantics(string provider, bool ignoreCase)
    {
        var clients = new ChatClientProvider(Configuration(provider));
        Assert.Same(clients.Get(null), clients.Get(" test-model "));
        Assert.Same(clients.Get(null), clients.Get(" "));
        Assert.Equal(ignoreCase, ReferenceEquals(clients.Get("test-model"), clients.Get("TEST-MODEL")));
    }

    [Fact]
    public async Task AzureRequestsRetainDeploymentPathsAndApiKeyAuthentication()
    {
        using var handler = new ResponseHandler(request =>
        {
            Assert.Equal("/openai/deployments/test-model/chat/completions", request.RequestUri!.AbsolutePath);
            Assert.Contains("api-version=", request.RequestUri.Query);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("api-key")));
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(FinalResponse(false));
        });
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(new ModelConnectionOptions(Configuration("AzureOpenAI")),
            new HttpClientPipelineTransport(httpClient)).Create();

        Assert.Equal("Done", (await client.GetResponseAsync("Hello")).Text);
    }

    [Theory]
    [InlineData("AzureOpenAI", false)]
    [InlineData("AzureOpenAI", true)]
    [InlineData("OpenAI", false)]
    [InlineData("OpenAI", true)]
    [InlineData("Gemini", false)]
    [InlineData("Gemini", true)]
    public async Task InFlightRequestsHonorCancellation(string provider, bool streaming)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ResponseHandler(request => { started.TrySetResult(); return pending.Task; });
        using var httpClient = new HttpClient(handler);
        using var client = new ModelClientFactory(new ModelConnectionOptions(Configuration(provider)),
            new HttpClientPipelineTransport(httpClient)).Create();
        using var cancellation = new CancellationTokenSource();
        var operation = streaming
            ? client.GetStreamingResponseAsync("Hello", cancellationToken: cancellation.Token)
                .ToChatResponseAsync(cancellationToken: cancellation.Token)
            : client.GetResponseAsync("Hello", cancellationToken: cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    [Theory]
    [InlineData("AzureOpenAI:Endpoint", "not-an-endpoint")]
    [InlineData("AzureOpenAI:Endpoint", "file:///private/secret")]
    [InlineData("AzureOpenAI:Endpoint", "https://user:secret@example.com/")]
    [InlineData("Models:ForceDefaultModel", "secret")]
    [InlineData("AzureOpenAI:ForceDefaultModel", "secret")]
    public void InvalidSettingsReportTheirNameWithoutTheirValue(string setting, string value)
    {
        var configuration = Configuration("AzureOpenAI");
        configuration[setting] = value;

        var error = Assert.Throws<InvalidOperationException>(() => new ModelConnectionOptions(configuration));

        Assert.Contains(setting, error.Message);
        Assert.DoesNotContain(value, error.Message);
        Assert.DoesNotContain("test-key", error.Message);
    }

    private static IConfigurationRoot Configuration(string provider) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Models:Provider"] = provider,
            [$"{provider}:ApiKey"] = "test-key",
            [$"{provider}:{(provider == "AzureOpenAI" ? "Deployment" : "Model")}"] = "test-model",
            [$"{provider}:Endpoint"] = "https://example.openai.azure.com/",
        }).Build();

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage ToolResponse(bool streaming) => streaming ? StreamResponse("""
        data: {"id":"tools-1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"EchoValue","arguments":"{\"value\":"},"extra_content":{"google":{"thought_signature":"opaque-signature-1"}}},{"index":1,"id":"call-2","type":"function","function":{"name":"EchoValue","arguments":"{\"value\":"}}]},"finish_reason":null}]}

        data: {"id":"tools-1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"first\"}"}},{"index":1,"function":{"arguments":"\"second\"}"}}]},"finish_reason":null}]}

        data: {"id":"tools-1","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}

        data: [DONE]

        """) : JsonResponse("""
        {"id":"tools-1","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","type":"function","function":{"name":"EchoValue","arguments":"{\"value\":\"first\"}"},"extra_content":{"google":{"thought_signature":"opaque-signature-1"}}},{"id":"call-2","type":"function","function":{"name":"EchoValue","arguments":"{\"value\":\"second\"}"}}]},"finish_reason":"tool_calls"}]}
        """);

    private static HttpResponseMessage FinalResponse(bool streaming) => streaming ? StreamResponse("""
        data: {"id":"reply-2","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"role":"assistant","content":"Do"},"finish_reason":null}]}

        data: {"id":"reply-2","object":"chat.completion.chunk","created":1,"model":"test-model","choices":[{"index":0,"delta":{"content":"ne"},"finish_reason":"stop"}]}

        data: [DONE]

        """) : JsonResponse("""
        {"id":"reply-2","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"Done"},"finish_reason":"stop"}]}
        """);

    private static HttpResponseMessage StreamResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "text/event-stream"),
    };

    private sealed class ResponseHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request).WaitAsync(cancellationToken);
    }

    private sealed class ModelDefinition(string? model) : AgentDefinitionBase
    {
        public override string Name => "Coder";
        public override string Description => "Model routing test";
        protected override string Persona => "Test";
        public override string? ModelId => model;
        public override IList<AITool> Tools => [];
    }
}