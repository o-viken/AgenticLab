using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheSeries.AiService.Application;

/// <summary>
/// Discovers the user-authored agents declared in the active <see cref="WorkspaceScope"/>, from two
/// conventions:
/// <list type="bullet">
/// <item>a top-level <c>agents/</c> folder, one <c>&lt;name&gt;.agent.yaml</c> file each, whose YAML body
/// declares a <c>name</c>, <c>description</c>, <c>persona</c> and a list of <c>tools</c> (the names of
/// existing backend tool functions the agent may call):
/// <code>
/// name: Reviewer
/// description: Reviews workspace code for risks without changing anything.
/// tools:
///   - ReadFile
///   - ListFiles
/// risk: Low      # optional; defaults from the tool set
/// skills: false  # optional; opt into workspace skills
/// model: gpt-5.3-codex  # optional; the Azure OpenAI deployment to run on (defaults to the global default)
/// persona: |
///   You are a meticulous code reviewer...
/// </code>
/// </item>
/// <item>the real GitHub Copilot/VS Code custom-agent convention: a <c>.github/agents/</c> folder, one
/// <c>&lt;name&gt;.md</c> file each, with an optional YAML frontmatter block (<c>name</c>,
/// <c>description</c>, <c>tools</c>, and the same optional <c>risk</c>/<c>skills</c>/<c>model</c>/
/// <c>guardrails</c> keys as above) followed by a markdown body used verbatim as the persona:
/// <code>
/// ---
/// name: mcp-agent
/// description: Describe what this custom agent does and when to use it.
/// tools: [read/readFile, search/fileSearch, search/listDirectory, web/fetch]
/// ---
/// (markdown body = persona/instructions)
/// </code>
/// Declared tool tokens are VS Code's own names (optionally namespaced, e.g. <c>search/fileSearch</c>);
/// they are mapped to this app's backend tool names via <see cref="ToolAliases"/> (matched on the segment
/// after the last <c>/</c>), and any token with no known mapping (e.g. <c>web/fetch</c>, which has no
/// backend equivalent) is dropped silently — <see cref="ToolAliases"/> is intentionally small and meant
/// to be extended as more tokens are encountered. The file must declare a name or have one derived from
/// its file name, and must have a non-empty body to be discovered.
/// </item>
/// </list>
/// When an agent name is declared in both conventions, the <c>agents/*.agent.yaml</c> definition wins.
/// Every path is resolved through the workspace scope, so discovery stays confined to the workspace.
/// Registered as a singleton; it reads the ambient workspace scope on each call and never caches.
/// A file that is missing a name (or, for markdown, a body) or fails to parse is skipped rather than
/// aborting discovery of the rest.
/// </summary>
public sealed class WorkspaceAgentLoader
{
    private const string FileSuffix = ".agent.yaml";

    // The folders scanned for the pure-YAML `<name>.agent.yaml` convention (a top-level `agents/` folder
    // plus the dotted `.agents/` folder).
    private static readonly string[] YamlAgentFolders = { "agents", ".agents" };

    // The folders scanned for the markdown (frontmatter + body-as-persona) convention, each paired with
    // the file suffix that marks an agent file there. Covers GitHub Copilot/VS Code (`.github/agents` and
    // `.github/chatmodes`), Claude Code (`.claude/agents`) and the dotted `.agents` folder.
    private static readonly (string Folder, string Suffix)[] MarkdownAgentSources =
    {
        (".github/agents", ".md"),
        (".github/chatmodes", ".chatmode.md"),
        (".claude/agents", ".md"),
        (".agents", ".md"),
    };

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // Maps VS Code/GitHub Copilot custom-agent tool tokens (optionally "namespace/tool") to this app's
    // backend tool names. Only the segment after the last '/' is matched, case-insensitively. Extend this
    // table as more VS Code tool tokens need to be recognized; an unmapped token (e.g. "web/fetch", which
    // has no backend equivalent) is dropped rather than granting nothing in its place.
    private static readonly Dictionary<string, string> ToolAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["readFile"] = "ReadFile",
        ["read"] = "ReadFile",
        ["fileSearch"] = "ListFiles",
        ["listDirectory"] = "ListFiles",
        ["list"] = "ListFiles",
        ["search"] = "ListFiles",
        ["editFile"] = "WriteFile",
        ["edit"] = "WriteFile",
        ["createFile"] = "WriteFile",
        ["applyPatch"] = "WriteFile",
        ["write"] = "WriteFile",
        ["multiEdit"] = "WriteFile",
        ["deleteFile"] = "DeleteFile",
        ["delete"] = "DeleteFile",
        ["runCommands"] = "RunCommand",
        ["runInTerminal"] = "RunCommand",
        ["terminal"] = "RunCommand",
        ["shell"] = "RunCommand",
        ["runTasks"] = "RunCommand",
        ["bash"] = "RunCommand",
        ["glob"] = "ListFiles",
        ["grep"] = "ListFiles",
        ["ls"] = "ListFiles",
        ["readSkill"] = "ReadSkill",
        ["askQuestion"] = "AskQuestion",
        ["ask"] = "AskQuestion",
        ["fetch"] = "WebFetch",
        ["webFetch"] = "WebFetch",
        ["web"] = "WebFetch",
    };

    /// <summary>
    /// Loads the agents declared in the active workspace, from both the <c>agents/*.agent.yaml</c> and
    /// <c>.github/agents/*.md</c> conventions. Returns an empty list when no workspace is active or when
    /// neither folder declares any agent. When the same name is declared in both, the <c>.agent.yaml</c>
    /// definition wins.
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

    // Scans the pure-YAML `<name>.agent.yaml` convention across every configured folder.
    private static IEnumerable<WorkspaceAgentDefinition> LoadYamlAgents(WorkspaceScope scope)
    {
        var agents = new List<WorkspaceAgentDefinition>();
        foreach (var folder in YamlAgentFolders)
        {
            var agentsRoot = scope.ResolvePath(folder);
            if (!Directory.Exists(agentsRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(agentsRoot, "*" + FileSuffix, SearchOption.TopDirectoryOnly))
            {
                var definition = TryParseYaml(file, folder + "/" + Path.GetFileName(file));
                if (definition is not null)
                {
                    agents.Add(definition);
                }
            }
        }

        return agents;
    }

    // Scans the markdown (frontmatter + body-as-persona) convention across every configured folder/suffix
    // (the real GitHub Copilot/VS Code, Claude Code and dotted-`.agents` custom-agent formats).
    private static IEnumerable<WorkspaceAgentDefinition> LoadMarkdownAgents(WorkspaceScope scope)
    {
        var agents = new List<WorkspaceAgentDefinition>();
        foreach (var (folder, suffix) in MarkdownAgentSources)
        {
            var agentsRoot = scope.ResolvePath(folder);
            if (!Directory.Exists(agentsRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(agentsRoot, "*" + suffix, SearchOption.TopDirectoryOnly))
            {
                var definition = TryParseMarkdown(file, folder + "/" + Path.GetFileName(file));
                if (definition is not null)
                {
                    agents.Add(definition);
                }
            }
        }

        return agents;
    }

    // Parses one .agent.yaml file into a definition, returning null when it is empty, unnamed or malformed.
    private static WorkspaceAgentDefinition? TryParseYaml(string file, string relativePath)
    {
        YamlAgent? yaml;
        try
        {
            var text = File.ReadAllText(file);
            yaml = Deserializer.Deserialize<YamlAgent>(text);
        }
        catch
        {
            // A malformed file should not abort discovery of the others.
            return null;
        }

        if (yaml is null || string.IsNullOrWhiteSpace(yaml.Name))
        {
            return null;
        }

        var tools = (yaml.Tools ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList();

        var risk = ParseRisk(yaml.Risk, tools);
        var guardrails = yaml.Guardrails is { Count: > 0 }
            ? yaml.Guardrails.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList()
            : DefaultGuardrails(tools);

        // The .agent.yaml convention declares backend tool names directly, so each mapping is an identity.
        var mappings = tools.Select(t => new ToolMapping(t, t)).ToList();

        return new WorkspaceAgentDefinition(
            yaml.Name.Trim(),
            (yaml.Description ?? string.Empty).Trim(),
            (yaml.Persona ?? string.Empty).Trim(),
            tools,
            risk,
            guardrails,
            yaml.Skills,
            string.IsNullOrWhiteSpace(yaml.Model) ? null : yaml.Model.Trim(),
            relativePath,
            mappings);
    }

    // Parses one .github/agents/*.md file: the leading '---'-delimited frontmatter (name, description,
    // tools, and the optional risk/skills/model/guardrails keys) plus the remaining markdown body, used
    // verbatim as the persona. Returns null when there is no well-formed frontmatter block or the body is
    // empty, so a malformed file does not abort discovery of the others.
    private static WorkspaceAgentDefinition? TryParseMarkdown(string file, string relativePath)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch
        {
            return null;
        }

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return null;
        }

        var frontmatterLines = new List<string>();
        var bodyStart = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                bodyStart = i + 1;
                break;
            }

            frontmatterLines.Add(lines[i]);
        }

        if (bodyStart < 0)
        {
            // Unterminated frontmatter block.
            return null;
        }

        YamlAgent? yaml;
        try
        {
            yaml = Deserializer.Deserialize<YamlAgent>(string.Join('\n', frontmatterLines));
        }
        catch
        {
            return null;
        }

        var body = bodyStart < lines.Length
            ? string.Join('\n', lines[bodyStart..]).Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(yaml?.Name)
            ? Path.GetFileNameWithoutExtension(file)
            : yaml.Name.Trim();

        var (tools, mappings) = MapToolAliases(yaml?.Tools ?? []);

        var risk = ParseRisk(yaml?.Risk, tools);
        var guardrails = yaml?.Guardrails is { Count: > 0 }
            ? yaml.Guardrails.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList()
            : DefaultGuardrails(tools);

        return new WorkspaceAgentDefinition(
            name,
            (yaml?.Description ?? string.Empty).Trim(),
            body,
            tools,
            risk,
            guardrails,
            yaml?.Skills ?? false,
            string.IsNullOrWhiteSpace(yaml?.Model) ? null : yaml.Model!.Trim(),
            relativePath,
            mappings);
    }

    // Maps declared tool tokens (VS Code-style, optionally "namespace/tool") to this app's backend tool
    // names via ToolAliases, matching on the segment after the last '/'. Returns both the deduplicated
    // list of resolved backend tools and one ToolMapping per declared token (with a null target for a
    // token that had no known mapping and was therefore dropped), so callers can surface the mapping.
    private static (IReadOnlyList<string> Tools, IReadOnlyList<ToolMapping> Mappings) MapToolAliases(IEnumerable<string> tokens)
    {
        var mapped = new List<string>();
        var mappings = new List<ToolMapping>();
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var declared = token.Trim();
            var key = declared;
            var slash = key.LastIndexOf('/');
            if (slash >= 0)
            {
                key = key[(slash + 1)..];
            }

            if (ToolAliases.TryGetValue(key, out var backendName))
            {
                mappings.Add(new ToolMapping(declared, backendName));
                if (!mapped.Contains(backendName, StringComparer.OrdinalIgnoreCase))
                {
                    mapped.Add(backendName);
                }
            }
            else
            {
                // No backend equivalent (e.g. web/fetch when no fetch tool exists): dropped, but recorded.
                mappings.Add(new ToolMapping(declared, null));
            }
        }

        return (mapped, mappings);
    }

    // Uses the declared risk when valid; otherwise infers it from whether the agent can change the workspace.
    private static AgentRiskLevel ParseRisk(string? declared, IReadOnlyList<string> tools)
    {
        if (!string.IsNullOrWhiteSpace(declared) &&
            Enum.TryParse<AgentRiskLevel>(declared.Trim(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return HasWriteAccess(tools) ? AgentRiskLevel.High : AgentRiskLevel.Low;
    }

    // The guardrails enforced for a workspace agent, derived from the tools it was granted.
    private static IReadOnlyList<string> DefaultGuardrails(IReadOnlyList<string> tools)
    {
        var guardrails = new List<string> { "Confined to the workspace folder (no ../ or absolute-path escape)" };

        if (tools.Any(t => t.Equals("RunCommand", StringComparison.OrdinalIgnoreCase)))
        {
            guardrails.Add("Commands restricted to an allowlist of executables");
            guardrails.Add("Shell-operator chaining (& | ; etc.) is rejected");
            guardrails.Add("Commands time out after 60 seconds");
        }

        if (tools.Any(t => t.Equals("WriteFile", StringComparison.OrdinalIgnoreCase) ||
                           t.Equals("DeleteFile", StringComparison.OrdinalIgnoreCase)))
        {
            guardrails.Add("File tools operate on files only — cannot delete directories");
        }

        return guardrails;
    }

    private static bool HasWriteAccess(IReadOnlyList<string> tools) =>
        tools.Any(t =>
            t.Equals("WriteFile", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("DeleteFile", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("RunCommand", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("SendMail", StringComparison.OrdinalIgnoreCase));

    // The shape deserialized from an .agent.yaml file or a .md file's frontmatter. Names map to camelCase
    // YAML keys; the markdown convention simply omits Persona (the body is used instead).
    private sealed class YamlAgent
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Persona { get; set; }
        public List<string>? Tools { get; set; }
        public string? Risk { get; set; }
        public bool Skills { get; set; }
        public string? Model { get; set; }
        public List<string>? Guardrails { get; set; }
    }
}
