namespace TheSeries.AiService.Application;

/// <summary>
/// An ambient, per-run scope carrying the instruction names the caller opted <em>in</em> for the current
/// run. Unlike tools and skills, custom instructions default to <em>off</em> (opt-in), so this holds an
/// allow-list: only instructions whose name is enabled here are injected into the agent's context. When
/// no scope is active, no instructions are injected. Like <see cref="ToolFilterScope"/> it lives in an
/// <see cref="AsyncLocal{T}"/> so concurrent runs stay isolated, and must be re-activated before each
/// agent advance in a streaming run (the AsyncLocal resets when the owning iterator resumes after a
/// <c>yield</c>).
/// </summary>
public sealed class InstructionFilterScope : IDisposable
{
    private static readonly AsyncLocal<InstructionFilterScope?> CurrentScope = new();
    private readonly HashSet<string> _enabled;

    private InstructionFilterScope(HashSet<string> enabled) => _enabled = enabled;

    /// <summary>The scope active on the current async context, or null when none is active.</summary>
    public static InstructionFilterScope? Current => CurrentScope.Value;

    /// <summary>Whether the named instruction was opted in for this run (matched case-insensitively).</summary>
    /// <param name="name">The instruction name to test.</param>
    /// <returns><c>true</c> when the instruction is enabled for this run.</returns>
    public bool IsEnabled(string name) => _enabled.Contains(name);

    /// <summary>
    /// Starts a new instruction-filter scope on the current async context for the given enabled instruction
    /// names. Dispose to end it.
    /// </summary>
    /// <param name="enabledInstructions">The names of the instructions to inject for this run.</param>
    /// <returns>The newly started scope.</returns>
    public static InstructionFilterScope Begin(IEnumerable<string> enabledInstructions)
    {
        var enabled = new HashSet<string>(
            enabledInstructions.Where(static name => !string.IsNullOrWhiteSpace(name)).Select(static name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var scope = new InstructionFilterScope(enabled);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>Re-asserts this scope as the active one on the current async context (see class remarks).</summary>
    public void Activate() => CurrentScope.Value = this;

    /// <summary>Ends this scope, clearing it from the current async context when it is still active.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            CurrentScope.Value = null;
        }
    }
}
