using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AgenticLab.ModelProviders;

#pragma warning disable SCME0001

/// <summary>Retains opaque Gemini tool metadata in conversation content; streamed metadata is scoped to one response.</summary>
internal sealed class GeminiChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private const string ExtraContentKey = "gemini.extra_content";

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(WithToolMetadata(messages), options, cancellationToken).ConfigureAwait(false);
        foreach (var call in response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>())
        {
            if (call.RawRepresentation is ChatToolCall raw && ReadExtra(raw.Patch) is { } extra)
                (call.AdditionalProperties ??= [])[ExtraContentKey] = extra;
        }
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var metadata = new Dictionary<int, (string? CallId, JsonElement? Extra)>();
        await foreach (var update in base.GetStreamingResponseAsync(WithToolMetadata(messages), options, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (update.RawRepresentation is StreamingChatCompletionUpdate raw)
            {
                foreach (var tool in raw.ToolCallUpdates)
                {
                    var previous = metadata.GetValueOrDefault(tool.Index);
                    metadata[tool.Index] = (tool.ToolCallId ?? previous.CallId, ReadExtra(tool.Patch) ?? previous.Extra);
                }
            }
            foreach (var call in update.Contents.OfType<FunctionCallContent>())
            {
                if (metadata.Values.FirstOrDefault(item => item.CallId == call.CallId).Extra is { } extra)
                    (call.AdditionalProperties ??= [])[ExtraContentKey] = extra;
            }
            yield return update;
        }
    }

    private static IEnumerable<ChatMessage> WithToolMetadata(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            var calls = message.Contents.OfType<FunctionCallContent>().ToArray();
            if (message.Role != ChatRole.Assistant || !calls.Any(call => ExtraContent(call) is not null))
            {
                yield return message;
                continue;
            }

            var tools = new List<ChatToolCall>(calls.Length);
            foreach (var call in calls)
            {
                var tool = ChatToolCall.CreateFunctionToolCall(call.CallId, call.Name,
                    BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(call.Arguments)));
                if (ExtraContent(call) is { } extra)
                    tool.Patch.Set("$.extra_content"u8, JsonSerializer.SerializeToUtf8Bytes(extra));
                tools.Add(tool);
            }
            var assistant = new AssistantChatMessage(tools);
            foreach (var text in message.Contents.OfType<TextContent>())
                assistant.Content.Add(ChatMessageContentPart.CreateTextPart(text.Text));
            assistant.Refusal = message.Contents.OfType<ErrorContent>()
                .FirstOrDefault(error => error.ErrorCode == "Refusal")?.Message;
            var outgoing = message.Clone();
            outgoing.RawRepresentation = assistant;
            yield return outgoing;
        }
    }

    private static JsonElement? ExtraContent(FunctionCallContent call) =>
        call.AdditionalProperties?.TryGetValue(ExtraContentKey, out var value) is true && value is JsonElement extra
            ? extra : null;

    private static JsonElement? ReadExtra(in JsonPatch patch)
    {
        if (!patch.TryGetJson("$.extra_content"u8, out var json)) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

#pragma warning restore SCME0001