namespace AgenticLab.Web.Flow;

/// <summary>
/// The per-run options sent with each chat request: the stepping mode and auto delay, the selected
/// execution breakpoints, and the tool / skill / custom-instruction toggles. Tools and skills default on
/// (a disabled set is tracked); instructions default off (an enabled set is tracked). The toggle sets are
/// agent-specific and reset when the selected agent changes.
/// </summary>
internal sealed class RunOptions(Action notify)
{
    private readonly HashSet<string> _disabledTools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabledSkills = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enabledInstructions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _breakpoints = new(StringComparer.Ordinal);
    private int _stepDelayMs = 600;
    private FlowMode _mode = FlowMode.Auto;

    /// <summary>The auto-mode server-side delay applied before each step, in milliseconds.</summary>
    public int StepDelayMs
    {
        get => _stepDelayMs;
        set { _stepDelayMs = value; notify(); }
    }

    /// <summary>Whether the run is paced automatically or one step per click.</summary>
    public FlowMode Mode
    {
        get => _mode;
        set { _mode = value; notify(); }
    }

    // --- Breakpoints -------------------------------------------------------

    /// <summary>The selectable execution boundaries and their labels.</summary>
    public static IReadOnlyList<(string Kind, string Label)> BreakpointOptions { get; } =
    [
        ("before-model", "Before model request"), ("after-model", "After model response"),
        ("before-tool", "Before tool execution"), ("after-tool", "After tool result"),
    ];

    /// <summary>The human-readable label of a breakpoint kind, or the kind itself when unknown.</summary>
    public static string BreakpointLabel(string kind) =>
        BreakpointOptions.FirstOrDefault(option => option.Kind == kind).Label ?? kind;

    /// <summary>The breakpoints selected for this page lifetime; not persisted across refreshes.</summary>
    public IReadOnlyList<string> Breakpoints => _breakpoints.ToArray();

    public bool IsBreakpointEnabled(string kind) => _breakpoints.Contains(kind);

    public void SetBreakpoint(string kind, bool enabled)
    {
        if (enabled) _breakpoints.Add(kind);
        else _breakpoints.Remove(kind);
        notify();
    }

    // --- Tools (default on) ------------------------------------------------

    public bool IsToolEnabled(string tool) => !_disabledTools.Contains(tool);

    public void SetToolEnabled(string tool, bool enabled)
    {
        if (enabled) _disabledTools.Remove(tool);
        else _disabledTools.Add(tool);
        notify();
    }

    /// <summary>The disabled tool names for the next run, or null when none are disabled.</summary>
    public IReadOnlyList<string>? DisabledToolsOrNull =>
        _disabledTools.Count > 0 ? _disabledTools.ToArray() : null;

    // --- Skills (default on) -----------------------------------------------

    public bool IsSkillEnabled(string skill) => !_disabledSkills.Contains(skill);

    public void SetSkillEnabled(string skill, bool enabled)
    {
        if (enabled) _disabledSkills.Remove(skill);
        else _disabledSkills.Add(skill);
        notify();
    }

    /// <summary>The disabled skill names for the next run, or null when none are disabled.</summary>
    public IReadOnlyList<string>? DisabledSkillsOrNull =>
        _disabledSkills.Count > 0 ? _disabledSkills.ToArray() : null;

    // --- Custom instructions (default off) ---------------------------------

    public bool IsInstructionEnabled(string instruction) => _enabledInstructions.Contains(instruction);

    public void SetInstructionEnabled(string instruction, bool enabled)
    {
        if (enabled) _enabledInstructions.Add(instruction);
        else _enabledInstructions.Remove(instruction);
        notify();
    }

    /// <summary>The enabled custom-instruction names for the next run, or null when none are enabled.</summary>
    public IReadOnlyList<string>? EnabledInstructionsOrNull =>
        _enabledInstructions.Count > 0 ? _enabledInstructions.ToArray() : null;

    /// <summary>Clears the agent-specific toggle sets (tool/skill/instruction names differ per agent).</summary>
    internal void ResetForAgent()
    {
        _disabledTools.Clear();
        _disabledSkills.Clear();
        _enabledInstructions.Clear();
    }
}
