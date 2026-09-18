namespace TheSeries.AiService.Application.Workspace;

/// <summary>
/// Discovers the user-authored agents declared in the active <see cref="WorkspaceScope"/>, from two
/// conventions:
/// <list type="bullet">
/// <item>a top-level <c>agents/</c> (or <c>.agents/</c>) folder, one <c>&lt;name&gt;.agent.yaml</c> file
/// each, whose YAML declares a <c>name</c>, <c>description</c>, <c>persona</c> and a list of <c>tools</c>
/// (the names of existing backend tool functions), plus optional <c>risk</c>/<c>skills</c>/<c>model</c>/
/// <c>guardrails</c> keys;</item>
/// <item>the real GitHub Copilot / VS Code / Claude Code custom-agent conventions (<c>.github/agents</c>,
/// <c>.github/chatmodes</c>, <c>.claude/agents</c>, <c>.agents</c>): markdown files with an optional YAML
/// frontmatter and a body used verbatim as the persona. Their VS Code-style tool tokens are mapped to
/// backend tools via <see cref="WorkspaceToolAliases"/>.</item>
/// </list>
/// When an agent name is declared in both conventions, the <c>agents/*.agent.yaml</c> definition wins.
/// Every path is resolved through the workspace scope, so discovery stays confined to the workspace.
/// Registered as a singleton; it reads the ambient workspace scope on each call and never caches. Parsing
/// of an individual file is delegated to <see cref="WorkspaceAgentFileParser"/>, which skips unnamed,
/// empty or malformed files rather than aborting discovery of the rest.
/// </summary>
public sealed class WorkspaceAgentLoader
{
    private const string FileSuffix = ".agent.yaml";

    private static readonly string[] YamlAgentFolders = { "agents", ".agents" };

    private static readonly (string Folder, string Suffix)[] MarkdownAgentSources =
    {
        (".github/agents", ".md"),
        (".github/chatmodes", ".chatmode.md"),
        (".claude/agents", ".md"),
        (".agents", ".md"),
    };

    /// <summary>
    /// Loads the agents declared in the active workspace from both conventions. Returns an empty list when
    /// no workspace is active or when neither folder declares any agent.
    /// </summary>
    /// <returns>The discovered agents, ordered by name (case-insensitive).</returns>
    internal IReadOnlyList<WorkspaceAgentDefinition> Load()
    {
        var scope = WorkspaceScope.Current;
        if (scope is null)
        {
            return [];
        }

        return LoadYamlAgents(scope)
            .Concat(LoadMarkdownAgents(scope))
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<WorkspaceAgentDefinition> LoadYamlAgents(WorkspaceScope scope)
    {
        foreach (var folder in YamlAgentFolders)
        {
            foreach (var definition in Scan(scope, folder, FileSuffix, WorkspaceAgentFileParser.TryParseYaml))
            {
                yield return definition;
            }
        }
    }

    private static IEnumerable<WorkspaceAgentDefinition> LoadMarkdownAgents(WorkspaceScope scope)
    {
        foreach (var (folder, suffix) in MarkdownAgentSources)
        {
            foreach (var definition in Scan(scope, folder, suffix, WorkspaceAgentFileParser.TryParseMarkdown))
            {
                yield return definition;
            }
        }
    }

    private static IEnumerable<WorkspaceAgentDefinition> Scan(
        WorkspaceScope scope, string folder, string suffix, Func<string, string, WorkspaceAgentDefinition?> parse)
    {
        var agentsRoot = scope.ResolvePath(folder);
        if (!Directory.Exists(agentsRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(agentsRoot, "*" + suffix, SearchOption.TopDirectoryOnly))
        {
            if (parse(file, folder + "/" + Path.GetFileName(file)) is { } definition)
            {
                yield return definition;
            }
        }
    }
}
