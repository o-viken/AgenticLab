using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheSeries.AiService.Application;

/// <summary>
/// Discovers the user-authored agents declared in the active <see cref="WorkspaceScope"/>. Agents live
/// in a top-level <c>agents/</c> folder, one <c>&lt;name&gt;.agent.yaml</c> file each, whose YAML body
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
/// Every path is resolved through the workspace scope, so discovery stays confined to the workspace.
/// Registered as a singleton; it reads the ambient workspace scope on each call and never caches.
/// A file that is missing a name or fails to parse is skipped rather than aborting discovery.
/// </summary>
public sealed class WorkspaceAgentLoader
{
    private const string AgentsFolder = "agents";
    private const string FileSuffix = ".agent.yaml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Loads the agents declared in the active workspace. Returns an empty list when no workspace is
    /// active, when the workspace has no <c>agents/</c> folder, or when no file declares a name.
    /// </summary>
    /// <returns>The discovered agents, ordered by name (case-insensitive).</returns>
    internal IReadOnlyList<WorkspaceAgentDefinition> Load()
    {
        var scope = WorkspaceScope.Current;
        if (scope is null)
        {
            return [];
        }

        var agentsRoot = scope.ResolvePath(AgentsFolder);
        if (!Directory.Exists(agentsRoot))
        {
            return [];
        }

        var agents = new List<WorkspaceAgentDefinition>();
        foreach (var file in Directory.EnumerateFiles(agentsRoot, "*" + FileSuffix, SearchOption.TopDirectoryOnly))
        {
            var definition = TryParse(file, AgentsFolder + "/" + Path.GetFileName(file));
            if (definition is not null)
            {
                agents.Add(definition);
            }
        }

        return agents
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Parses one .agent.yaml file into a definition, returning null when it is empty, unnamed or malformed.
    private static WorkspaceAgentDefinition? TryParse(string file, string relativePath)
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

        return new WorkspaceAgentDefinition(
            yaml.Name.Trim(),
            (yaml.Description ?? string.Empty).Trim(),
            (yaml.Persona ?? string.Empty).Trim(),
            tools,
            risk,
            guardrails,
            yaml.Skills,
            string.IsNullOrWhiteSpace(yaml.Model) ? null : yaml.Model.Trim(),
            relativePath);
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

    // The shape deserialized from an .agent.yaml file. Names map to camelCase YAML keys.
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
