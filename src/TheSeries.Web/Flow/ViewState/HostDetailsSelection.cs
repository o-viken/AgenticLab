namespace TheSeries.Web.Flow;

/// <summary>The host layer inspected independently of diagram visibility and execution.</summary>
public enum HostDetailSection
{
    /// <summary>The client and service responsibilities.</summary>
    Client,
    /// <summary>The selected host's instructions.</summary>
    SystemPrompt,
    /// <summary>Workspace-authored instruction metadata and captured composition.</summary>
    Instructions,
    /// <summary>The agent description and captured persona text.</summary>
    Persona,
    /// <summary>Configured capabilities and captured function definitions.</summary>
    Tools,
    /// <summary>Model configuration and host execution controls.</summary>
    Settings,
    /// <summary>The unsent draft and the displayed exchange's submitted message.</summary>
    UserPrompt,
    /// <summary>Conversation content through the displayed execution position.</summary>
    Context,
    /// <summary>Workspace skill metadata and captured loaded content.</summary>
    Skills,
    /// <summary>Tools discovered over MCP.</summary>
    Mcp,
    /// <summary>Remote agents discovered over A2A.</summary>
    A2A,
    /// <summary>Execution location, declared risk and guardrails.</summary>
    Environment
}

/// <summary>One transient inspector selection with its own dock size and collapse state.</summary>
internal sealed class HostDetailsSelection(Action reveal, Action notify)
{
    /// <summary>The sole selected detail, or null when Details is closed.</summary>
    public HostDetailSection? Section { get; private set; }

    /// <summary>Whether a detail is open, independently of Learn visibility.</summary>
    public bool Active => Section is not null;

    private bool _collapsed;
    private int _width = 320;

    /// <summary>Whether Details is collapsed to its own rail; the selection is retained.</summary>
    public bool Collapsed
    {
        get => _collapsed;
        set { _collapsed = value; notify(); }
    }

    /// <summary>Details width for this page lifetime; changing it never resizes Learn.</summary>
    public int Width
    {
        get => _width;
        set { _width = Math.Clamp(value, 240, 640); notify(); }
    }

    /// <summary>The independent grid track disappears when no detail is selected.</summary>
    public string PanelStyle => $"--details-w: {(!Active ? 0 : Collapsed ? 44 : Width)}px;";

    /// <summary>Explicit activations move focus; background data updates do not.</summary>
    public int Activation { get; private set; }

    /// <summary>Replaces the detail and expands only its own dock without changing the run or Learn.</summary>
    public void Open(HostDetailSection section)
    {
        Section = section;
        _collapsed = false;
        Activation++;
        reveal();
        notify();
    }

    /// <summary>Releases only the inspector selection.</summary>
    public void Close()
    {
        Section = null;
        notify();
    }

    /// <summary>Shared titles keep the compact host, anatomy and inspector consistent.</summary>
    public static string Title(HostDetailSection section) => section switch
    {
        HostDetailSection.SystemPrompt => "System prompt",
        HostDetailSection.Instructions => "Custom instructions",
        HostDetailSection.Persona => "Agent persona",
        HostDetailSection.UserPrompt => "User prompt",
        HostDetailSection.Mcp => "MCP servers",
        HostDetailSection.A2A => "A2A agents",
        HostDetailSection.Environment => "Environment & risk",
        _ => section.ToString()
    };

    /// <summary>The contributor rail shared with the expanded anatomy.</summary>
    public static string Contributor(HostDetailSection section) => section switch
    {
        HostDetailSection.Client or HostDetailSection.SystemPrompt or HostDetailSection.Environment => "app",
        HostDetailSection.Instructions or HostDetailSection.UserPrompt or HostDetailSection.Skills => "user",
        _ => "agent"
    };
}