namespace TheSeries.Web.Flow;

/// <summary>
/// The catalogues the harness boxes show before and during a run — the workspace's skills (and which the
/// model has loaded), its custom instructions, the MCP-discovered tools, the reachable A2A agents — and the
/// refreshers that reload them (plus the workspace agent roster, repo suggestions and harness prompt on the
/// view) when the agent, vendor or workspace changes. Every fetch is best-effort: the lists are
/// informational and a path may still be mid-edit.
/// </summary>
internal sealed class WorkspaceCatalogs(AiServiceClient ai, FlowViewState view, Func<Task> notify)
{
    private readonly List<SkillChip> _knownSkills = new();
    private readonly HashSet<string> _loadedSkills = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<InstructionChip> _knownInstructions = new();
    private readonly List<McpChip> _knownMcp = new();
    private readonly List<A2AChip> _knownA2A = new();
    private int _skillsVersion = -1, _instructionsVersion = -1, _mcpVersion = -1, _a2aVersion = -1;
    private int _skillsRequest, _instructionsRequest, _mcpRequest, _a2aRequest, _promptRequest, _agentsRequest;
    private int _trackingVersion = -1;

    public IReadOnlyList<SkillChip> KnownSkills => _skillsVersion == view.ConfigurationVersion ? _knownSkills : [];

    /// <summary>The workspace's custom instructions the caller may enable per run.</summary>
    public IReadOnlyList<InstructionChip> KnownInstructions => _instructionsVersion == view.ConfigurationVersion ? _knownInstructions : [];

    public IReadOnlyList<McpChip> KnownMcp => _mcpVersion == view.ConfigurationVersion && view.Agent.SupportsMcp ? _knownMcp : [];

    /// <summary>The agents the selected agent can delegate to over A2A, shown in the harness A2A box.</summary>
    public IReadOnlyList<A2AChip> KnownA2A => _a2aVersion == view.ConfigurationVersion && view.Agent.SupportsA2A ? _knownA2A : [];

    public bool IsSkillLoaded(string name) => _skillsVersion == view.ConfigurationVersion && _loadedSkills.Contains(name);

    /// <summary>Forgets which skills the model loaded (a new run starts fresh).</summary>
    internal void ClearLoaded()
    {
        _trackingVersion = view.ConfigurationVersion;
        _loadedSkills.Clear();
    }

    // Keeps the Skills box in sync with the run: the catalogue (parsed from the first llm-request when
    // it wasn't fetched up front) and the skills the model has actually loaded via ReadSkill.
    internal void Track(FlowEvent flowEvent)
    {
        if (_trackingVersion != view.ConfigurationVersion) return;
        if (_skillsVersion != view.ConfigurationVersion)
        {
            _knownSkills.Clear();
            _skillsVersion = view.ConfigurationVersion;
        }
        if (flowEvent.Kind == "llm-request" && _knownSkills.Count == 0 && !string.IsNullOrWhiteSpace(flowEvent.Data))
        {
            _knownSkills.AddRange(SkillParsing.ParseKnownSkills(flowEvent.Data));
        }
        else if (flowEvent.Kind == "tool-call")
        {
            var loaded = SkillParsing.LoadedSkillFor(flowEvent, _knownSkills);
            if (loaded is not null)
            {
                _loadedSkills.Add(loaded);
            }
        }
    }

    /// <summary>Loads the workspace's skill catalogue; cleared when the agent does not use skills or no workspace is set.</summary>
    public async Task RefreshKnownSkillsAsync()
    {
        var version = _skillsVersion = view.ConfigurationVersion;
        var request = ++_skillsRequest;
        _knownSkills.Clear();
        _loadedSkills.Clear();

        if (!view.Agent.SupportsSkills || string.IsNullOrWhiteSpace(view.Workspace))
        {
            await notify();
            return;
        }

        try
        {
            var response = await ai.GetSkillsAsync(view.Workspace);
            if (version != view.ConfigurationVersion || request != _skillsRequest) return;
            if (response is not null)
            {
                _knownSkills.AddRange(response.Skills.Select(s => new SkillChip(s.Name, s.Description)));
            }
        }
        catch
        {
            // Best-effort: the list is informational and the path may still be mid-edit.
        }

        await notify();
    }

    /// <summary>Loads the workspace's custom instructions; cleared when the agent has no workspace or none is set.</summary>
    public async Task RefreshKnownInstructionsAsync()
    {
        var version = _instructionsVersion = view.ConfigurationVersion;
        var request = ++_instructionsRequest;
        _knownInstructions.Clear();

        if (!view.Agent.SupportsInstructions || string.IsNullOrWhiteSpace(view.Workspace))
        {
            await notify();
            return;
        }

        try
        {
            var response = await ai.GetInstructionsAsync(view.Workspace);
            if (version != view.ConfigurationVersion || request != _instructionsRequest) return;
            if (response is not null)
            {
                _knownInstructions.AddRange(response.Instructions.Select(i => new InstructionChip(i.Name, i.Description)));
            }
        }
        catch
        {
            // Best-effort: the list is informational and the path may still be mid-edit.
        }

        await notify();
    }

    /// <summary>Loads the workspace's user-authored agents for the picker; cleared when irrelevant for the vendor.</summary>
    public async Task RefreshWorkspaceAgentsAsync()
    {
        var version = view.ConfigurationVersion;
        var request = ++_agentsRequest;
        if (!view.Roster.VendorHasWorkspaceAgent || string.IsNullOrWhiteSpace(view.Workspace))
        {
            view.Roster.SetWorkspaceAgents(Array.Empty<AgentInfo>());
            return;
        }

        try
        {
            var response = await ai.GetWorkspaceAgentsAsync(view.Workspace);
            if (version != view.ConfigurationVersion || request != _agentsRequest) return;
            view.Roster.SetWorkspaceAgents(response?.Agents ?? Array.Empty<AgentInfo>());
        }
        catch
        {
            if (version != view.ConfigurationVersion || request != _agentsRequest) return;
            view.Roster.SetWorkspaceAgents(Array.Empty<AgentInfo>());
        }
    }

    /// <summary>Refreshes everything that depends on the workspace path: skills, instructions, workspace agents and the harness prompt.</summary>
    public async Task RefreshWorkspaceContextAsync()
    {
        var version = view.ConfigurationVersion;
        await RefreshKnownSkillsAsync();
        if (version != view.ConfigurationVersion) return;
        await RefreshKnownInstructionsAsync();
        if (version != view.ConfigurationVersion) return;
        await RefreshWorkspaceAgentsAsync();
        if (version != view.ConfigurationVersion) return;
        await RefreshKnownMcpAsync();
        if (version != view.ConfigurationVersion) return;
        await RefreshKnownA2AAsync();
        if (version != view.ConfigurationVersion) return;
        await RefreshHarnessPromptAsync();
    }

    /// <summary>Loads the repo sub-folders under the configured base folders for the workspace picker.</summary>
    public async Task RefreshWorkspaceSuggestionsAsync()
    {
        var bases = view.WorkspacePrefs.BasePaths;
        if (bases.Count == 0)
        {
            view.WorkspacePrefs.SetDirectories(Array.Empty<WorkspaceEntry>());
            return;
        }

        try
        {
            var response = await ai.GetWorkspaceDirectoriesAsync(bases);
            view.WorkspacePrefs.SetDirectories(response?.Directories ?? Array.Empty<WorkspaceEntry>());
        }
        catch
        {
            view.WorkspacePrefs.SetDirectories(Array.Empty<WorkspaceEntry>());
        }
    }

    /// <summary>Loads the active system (harness) prompt for the selected vendor and agent.</summary>
    public async Task RefreshHarnessPromptAsync()
    {
        var version = view.ConfigurationVersion;
        var request = ++_promptRequest;
        view.Harness.BeginPromptLoad();
        try
        {
            var response = await ai.GetHarnessAsync(view.SelectedAgent, view.VendorKey);
            if (version != view.ConfigurationVersion || request != _promptRequest) return;
            view.Harness.SetPrompt(response?.Prompt);
        }
        catch
        {
            if (version != view.ConfigurationVersion || request != _promptRequest) return;
            view.Harness.SetPrompt(null);
        }
    }

    /// <summary>Reacts to the selected agent changing: everything keyed on the agent is reloaded.</summary>
    public async Task OnAgentChangedAsync()
    {
        var version = view.ConfigurationVersion;
        await RefreshKnownSkillsAsync();
        if (version != view.ConfigurationVersion) return;
        await RefreshKnownInstructionsAsync();
        if (version != view.ConfigurationVersion) return;
        await RefreshKnownMcpAsync();
        if (version != view.ConfigurationVersion) return;
        await RefreshKnownA2AAsync();
        if (version != view.ConfigurationVersion) return;
        await RefreshHarnessPromptAsync();
    }

    /// <summary>Refreshes the MCP box with the tools the service discovered (global, not workspace-scoped).</summary>
    public async Task RefreshKnownMcpAsync()
    {
        var version = _mcpVersion = view.ConfigurationVersion;
        var request = ++_mcpRequest;
        _knownMcp.Clear();
        if (!view.Agent.SupportsMcp)
        {
            await notify();
            return;
        }

        try
        {
            var response = await ai.GetMcpAsync();
            if (version != view.ConfigurationVersion || request != _mcpRequest) return;
            if (response is not null)
            {
                _knownMcp.AddRange(response.Servers.SelectMany(s => s.Tools.Select(t => new McpChip(t.Name, t.Description))));
            }
        }
        catch
        {
            // Best-effort: the list is informational and the path may still be mid-edit.
        }

        await notify();
    }

    /// <summary>Refreshes the A2A box with the agents the service can reach (global, not workspace-scoped).</summary>
    public async Task RefreshKnownA2AAsync()
    {
        var version = _a2aVersion = view.ConfigurationVersion;
        var request = ++_a2aRequest;
        _knownA2A.Clear();
        if (!view.Agent.SupportsA2A)
        {
            await notify();
            return;
        }

        try
        {
            var response = await ai.GetA2AAsync();
            if (version != view.ConfigurationVersion || request != _a2aRequest) return;
            if (response is not null)
            {
                _knownA2A.AddRange(response.Agents.Select(a => new A2AChip(a.Name, a.Description)));
            }
        }
        catch
        {
            // Best-effort: the list is informational and the path may still be mid-edit.
        }

        await notify();
    }
}
