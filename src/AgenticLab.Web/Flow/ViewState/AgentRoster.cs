namespace AgenticLab.Web.Flow;

/// <summary>
/// The agents and vendors the page knows about: the service's registered agents, the workspace-discovered
/// agents, the brand-vendor metadata (loaded from <c>GET /vendors</c>), and the roster the Agent picker
/// offers for the current vendor.
/// </summary>
internal sealed class AgentRoster(FlowViewState owner, Action notify)
{
    private readonly List<AgentInfo> _agents = new();
    private readonly List<AgentInfo> _workspaceAgents = new();
    private readonly Dictionary<string, VendorInfo> _vendors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The agents the service registered (loaded once on initialise).</summary>
    public IReadOnlyList<AgentInfo> Agents => _agents;

    /// <summary>Replaces the known agents (called after loading them from the service).</summary>
    public void SetAgents(IEnumerable<AgentInfo> agents)
    {
        _agents.Clear();
        _agents.AddRange(agents);
        notify();
    }

    /// <summary>The user-authored agents discovered in the active workspace's agents/ folder.</summary>
    public IReadOnlyList<AgentInfo> WorkspaceAgents => _workspaceAgents;

    /// <summary>Replaces the workspace-discovered agents (called when the workspace changes).</summary>
    public void SetWorkspaceAgents(IEnumerable<AgentInfo> agents)
    {
        _workspaceAgents.Clear();
        _workspaceAgents.AddRange(agents);
        notify();
    }

    /// <summary>Replaces the brand-vendor metadata loaded from the service (keyed by the backend vendor key).</summary>
    public void SetVendors(IEnumerable<VendorInfo> vendors)
    {
        _vendors.Clear();
        foreach (var vendor in vendors)
        {
            _vendors[vendor.Key] = vendor;
        }

        notify();
    }

    /// <summary>Looks up a registered or workspace agent by name, or null when unknown.</summary>
    public AgentInfo? Find(string? name) =>
        _agents.FirstOrDefault(a => a.Name == name)
        ?? _workspaceAgents.FirstOrDefault(a => a.Name == name);

    /// <summary>The loaded metadata for the selected vendor, or null for a vendor without a harness key or before load.</summary>
    public VendorInfo? CurrentVendorInfo =>
        owner.VendorKey is { } key && _vendors.TryGetValue(key, out var info) ? info : null;

    /// <summary>The loaded metadata for the given vendor (used by the vendor rail's hover tooltips), or null before load.</summary>
    public VendorInfo? VendorInfoFor(Vendor vendor) =>
        VendorCatalog.HarnessKey(vendor) is { } key && _vendors.TryGetValue(key, out var info) ? info : null;

    /// <summary>The friendly display name of the given vendor, falling back to its enum name before metadata loads.</summary>
    public string VendorDisplayName(Vendor vendor) => VendorInfoFor(vendor)?.DisplayName ?? vendor.ToString();

    /// <summary>The friendly display name of the selected vendor (page title and header).</summary>
    public string VendorName => CurrentVendorInfo?.DisplayName ?? owner.Vendor.ToString();

    /// <summary>
    /// The agents offered by the current vendor, restricted to those the service actually registered (an
    /// unknown name in the roster is silently skipped). Order follows the vendor's modes.
    /// </summary>
    public IReadOnlyList<AgentChoice> AvailableAgents =>
        (CurrentVendorInfo?.Modes.Select(m => new AgentChoice(m.Agent, m.Label)) ?? Enumerable.Empty<AgentChoice>())
            .Where(c => _agents.Any(a => string.Equals(a.Name, c.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>The current vendor's default (first available) agent, or null when the vendor offers none.</summary>
    public string? VendorDefaultAgent => AvailableAgents.Count > 0 ? AvailableAgents[0].Name : null;

    /// <summary>Whether the current vendor offers a workspace-requiring agent, so workspace agents are relevant.</summary>
    public bool VendorHasWorkspaceAgent =>
        AvailableAgents.Any(c => _agents.Any(a =>
            string.Equals(a.Name, c.Name, StringComparison.OrdinalIgnoreCase) && a.RequiresWorkspace));

    /// <summary>The workspace-discovered agents as picker choices, appended after a separator; empty unless relevant.</summary>
    public IReadOnlyList<AgentChoice> WorkspaceAgentChoices =>
        VendorHasWorkspaceAgent
            ? _workspaceAgents.Select(a => new AgentChoice(a.Name, a.Name)).ToList()
            : Array.Empty<AgentChoice>();
}
