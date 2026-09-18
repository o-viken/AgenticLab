using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application.Flow;

/// <summary>
/// An ambient, per-run scope that carries the set of tool names the caller has disabled for the
/// current run, so the shared singleton chat client offers the model only the remaining tools.
/// Like <see cref="WorkspaceScope"/> and <see cref="FlowCaptureScope"/> it lives in an
/// <see cref="AsyncLocal{T}"/> so concurrent runs stay isolated.
/// </summary>
/// <remarks>
/// The agent framework only ever <em>unions</em> per-run tools with an agent's built-in tools — it never
/// removes them — so restricting an agent to a subset of its tools cannot be done through run options.
/// Instead the <see cref="ToolFilteringChatClient"/> reads this scope and strips the disabled tools from
/// <see cref="ChatOptions.Tools"/> before the request reaches function invocation and the model.
/// </remarks>
public sealed class ToolFilterScope : IDisposable
{
    private static readonly AsyncLocal<ToolFilterScope?> CurrentScope = new();
    private readonly HashSet<string> _disabled;

    private ToolFilterScope(HashSet<string> disabled) => _disabled = disabled;

    /// <summary>The scope active on the current async context, or null when no tools are disabled.</summary>
    public static ToolFilterScope? Current => CurrentScope.Value;

    /// <summary>The tool names disabled for this run, compared case-insensitively.</summary>
    public IReadOnlyCollection<string> DisabledNames => _disabled;

    /// <summary>
    /// Starts a new tool-filter scope on the current async context for the given disabled tool names.
    /// Dispose to end it.
    /// </summary>
    /// <param name="disabledTools">The names of the tools to hide from the model for this run.</param>
    /// <returns>The newly started scope.</returns>
    public static ToolFilterScope Begin(IEnumerable<string> disabledTools)
    {
        var disabled = new HashSet<string>(
            disabledTools.Where(static name => !string.IsNullOrWhiteSpace(name)).Select(static name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var scope = new ToolFilterScope(disabled);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Re-asserts this scope as the active one on the current async context. The value lives in an
    /// <see cref="AsyncLocal{T}"/>, which is reset whenever an owning async iterator resumes after a
    /// <c>yield return</c>; callers driving the agent through such an iterator must re-activate before
    /// each advance so the filter applies on every LLM round-trip.
    /// </summary>
    public void Activate() => CurrentScope.Value = this;

    /// <summary>
    /// Returns the tools that remain after removing the ones disabled for this run, matched by
    /// <see cref="AIFunction.Name"/> (case-insensitive). Non-function tools are always kept.
    /// </summary>
    /// <param name="tools">The agent's full tool set for this request.</param>
    /// <returns>The filtered tool list.</returns>
    public IList<AITool> Filter(IList<AITool> tools) =>
        _disabled.Count == 0
            ? tools
            : tools.Where(tool => tool is not AIFunction function || !_disabled.Contains(function.Name)).ToList();

    /// <summary>Ends the scope, clearing it from the current async context.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            CurrentScope.Value = null;
        }
    }
}
