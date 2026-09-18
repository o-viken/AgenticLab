using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application.Flow;

/// <summary>
/// A chat-client middleware that removes the tools disabled by the active <see cref="ToolFilterScope"/>
/// from <see cref="ChatOptions.Tools"/> before the request reaches function invocation and the model.
/// When no scope is active (or it disables nothing) it is a transparent pass-through, so the shared
/// singleton client only filters during a run that asked for it.
/// </summary>
/// <remarks>
/// This must sit <em>outside</em> the function-invocation middleware in the pipeline so the removed tools
/// are invisible both to the model and to the function-invocation loop that would otherwise be willing to
/// call them.
/// </remarks>
/// <param name="inner">The next client in the pipeline.</param>
public sealed class ToolFilteringChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, Filter(options), cancellationToken);

    /// <inheritdoc />
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, Filter(options), cancellationToken);

    // Returns the options unchanged unless a tool-filter scope is active with tools to remove, in which
    // case it returns a clone whose Tools list excludes the disabled tools.
    private static ChatOptions? Filter(ChatOptions? options)
    {
        if (ToolFilterScope.Current is not { } scope || options?.Tools is not { Count: > 0 } tools)
        {
            return options;
        }

        var filtered = scope.Filter(tools);
        if (filtered.Count == tools.Count)
        {
            return options;
        }

        var clone = options.Clone();
        clone.Tools = filtered;
        return clone;
    }
}
