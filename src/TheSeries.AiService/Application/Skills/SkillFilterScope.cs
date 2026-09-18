namespace TheSeries.AiService.Application.Skills;

/// <summary>
/// An ambient, per-run scope carrying the skill names the caller disabled for the current run. Skills
/// default to <em>enabled</em>, so this holds the ones to hide from the model — a disabled skill is
/// excluded from the <c>&lt;skills&gt;</c> catalogue and refused by <c>ReadSkill</c>. Like
/// <see cref="ToolFilterScope"/> it lives in an <see cref="AsyncLocal{T}"/> so concurrent runs stay
/// isolated, and must be re-activated before each agent advance in a streaming run (the AsyncLocal is
/// reset whenever the owning iterator resumes after a <c>yield</c>).
/// </summary>
public sealed class SkillFilterScope : IDisposable
{
    private static readonly AsyncLocal<SkillFilterScope?> CurrentScope = new();
    private readonly HashSet<string> _disabled;

    private SkillFilterScope(HashSet<string> disabled) => _disabled = disabled;

    /// <summary>The scope active on the current async context, or null when no skills are disabled.</summary>
    public static SkillFilterScope? Current => CurrentScope.Value;

    /// <summary>Whether the named skill is disabled for this run (matched case-insensitively).</summary>
    /// <param name="name">The skill name to test.</param>
    /// <returns><c>true</c> when the skill is disabled for this run.</returns>
    public bool IsDisabled(string name) => _disabled.Contains(name);

    /// <summary>
    /// Starts a new skill-filter scope on the current async context for the given disabled skill names.
    /// Dispose to end it.
    /// </summary>
    /// <param name="disabledSkills">The names of the skills to hide from the model for this run.</param>
    /// <returns>The newly started scope.</returns>
    public static SkillFilterScope Begin(IEnumerable<string> disabledSkills)
    {
        var disabled = new HashSet<string>(
            disabledSkills.Where(static name => !string.IsNullOrWhiteSpace(name)).Select(static name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var scope = new SkillFilterScope(disabled);
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
