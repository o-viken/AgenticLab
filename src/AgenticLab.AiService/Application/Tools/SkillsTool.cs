using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Tools;

/// <summary>
/// Exposes the workspace's skills to the coding agent. The names and descriptions of the available
/// skills are injected into the agent's context each run (see <see cref="SkillLoader.BuildContextBlock"/>);
/// this tool lets the agent then pull in a skill's full instructions on demand. The skill file is read
/// through the active <see cref="WorkspaceScope"/>, so access stays confined to the workspace folder.
/// </summary>
/// <param name="loader">Discovers the skills declared in the active workspace.</param>
/// <param name="matcher">Resolves the requested skill name to a discovered skill.</param>
public sealed class SkillsTool(SkillLoader loader, SkillMatcher matcher)
{
    /// <summary>Reads the full instructions of a named skill from the workspace.</summary>
    /// <param name="name">The name of the skill to load, as listed in the &lt;skills&gt; section.</param>
    /// <returns>The skill's full <c>SKILL.md</c> contents, or a message listing the available skills when not found.</returns>
    [Description("Load the full instructions for a named workspace skill. Call this with a skill name from the <skills> list before using that skill.")]
    public async Task<string> ReadSkill(
        [Description("The name of the skill to load, exactly as shown in the <skills> list, e.g. 'get-date'.")] string name,
        CancellationToken cancellationToken = default)
    {
        var skills = loader.LoadAvailable();
        if (skills.Count == 0)
        {
            return "No skills are available in this workspace.";
        }

        if (!matcher.TryMatch(skills, name, out var skill))
        {
            var available = string.Join(", ", skills.Select(s => s.Name));
            return $"No skill named '{name}' was found. Available skills: {available}.";
        }

        var full = WorkspaceScope.Require().ResolvePath(skill.RelativePath);
        if (!File.Exists(full))
        {
            return $"The skill '{skill.Name}' is listed but its file '{skill.RelativePath}' is missing.";
        }

        return await File.ReadAllTextAsync(full, cancellationToken);
    }

    /// <summary>Builds the <see cref="AITool"/> set this tool exposes to the agent.</summary>
    /// <returns>The skill-loading tool.</returns>
    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(ReadSkill),
    ];
}
