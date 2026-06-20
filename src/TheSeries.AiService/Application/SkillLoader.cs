namespace TheSeries.AiService.Application;

/// <summary>
/// Discovers the skills available in the active <see cref="WorkspaceScope"/>. Skills live under a
/// top-level <c>skills/</c> folder, one per sub-directory, each with a <c>SKILL.md</c> file whose YAML
/// frontmatter declares a <c>name</c> and <c>description</c>:
/// <code>
/// ---
/// name: get-date
/// description: Get the current date and time on a Windows machine via the terminal.
/// ---
/// (markdown body with the full instructions)
/// </code>
/// The loader reads only the lightweight name/description metadata so the catalogue can be injected into
/// the model's context cheaply; the full body is read later, on demand, by <c>SkillsTool.ReadSkill</c>.
/// All paths are resolved through the workspace scope, so discovery is confined to the workspace folder.
/// Registered as a singleton; it reads the ambient workspace scope on each call and never caches.
/// </summary>
public sealed class SkillLoader
{
    private const string SkillsFolder = "skills";
    private const string SkillFileName = "SKILL.md";

    /// <summary>
    /// Loads the skills declared in the active workspace. Returns an empty list when no workspace is
    /// active, when the workspace has no <c>skills/</c> folder, or when no skill declares a name.
    /// </summary>
    /// <returns>The discovered skills, ordered by name (case-insensitive).</returns>
    internal IReadOnlyList<SkillDefinition> Load()
    {
        var scope = WorkspaceScope.Current;
        if (scope is null)
        {
            return [];
        }

        var skillsRoot = scope.ResolvePath(SkillsFolder);
        if (!Directory.Exists(skillsRoot))
        {
            return [];
        }

        var skills = new List<SkillDefinition>();
        foreach (var dir in Directory.EnumerateDirectories(skillsRoot))
        {
            var file = Path.Combine(dir, SkillFileName);
            if (!File.Exists(file))
            {
                continue;
            }

            var (name, description) = ReadFrontmatter(file);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = Path.GetFileName(dir);
            skills.Add(new SkillDefinition(name, description, $"{SkillsFolder}/{id}/{SkillFileName}"));
        }

        return skills.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Builds the <c>&lt;skills&gt;</c> context block listing each available skill's name and description,
    /// for injection into the agent's instructions for a single run. Returns <c>null</c> when the active
    /// workspace declares no skills, so callers can skip injection entirely.
    /// </summary>
    /// <returns>The context block, or <c>null</c> when there are no skills.</returns>
    public string? BuildContextBlock()
    {
        var skills = Load();
        if (skills.Count == 0)
        {
            return null;
        }

        var lines = skills.Select(s => $"- {s.Name}: {s.Description}");
        return
            "<skills>\n" +
            "The following skills are available in this workspace. Each is a short, named playbook for a " +
            "specific task. When a skill matches what you are asked to do, call the ReadSkill tool with its " +
            "name to load its full instructions, then follow them. Do not guess a skill's contents from its " +
            "description alone.\n" +
            string.Join("\n", lines) +
            "\n</skills>";
    }

    // Parses the leading YAML frontmatter (the block delimited by '---' lines) for the 'name' and
    // 'description' keys. Intentionally minimal — no YAML dependency — since the frontmatter is a flat
    // set of simple key: value pairs. Returns blanks when the field or frontmatter is absent.
    private static (string Name, string Description) ReadFrontmatter(string file)
    {
        var lines = File.ReadAllLines(file);
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return (string.Empty, string.Empty);
        }

        var name = string.Empty;
        var description = string.Empty;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == "---")
            {
                break;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                name = value;
            }
            else if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                description = value;
            }
        }

        return (name, description);
    }
}
