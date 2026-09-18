namespace TheSeries.AiService.Application.Skills;

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
    private const string SkillFileName = "SKILL.md";

    // The folders scanned for the folder-per-skill convention (each sub-directory holds a SKILL.md). Covers
    // a top-level `skills/` folder, GitHub Copilot's `.github/skills/` and Claude Code's `.claude/skills/`.
    private static readonly string[] SkillFolders = { "skills", ".github/skills", ".claude/skills" };

    // The folders scanned for the flat, one-file-per-skill convention, each paired with the suffix that
    // marks a file there. The skill name comes from the file name; the full body is loaded on demand by
    // ReadSkill. Covers VS Code prompt files and Claude Code slash-command playbooks.
    private static readonly (string Folder, string Suffix)[] FlatSkillSources =
    {
        (".github/prompts", ".prompt.md"),
        (".claude/commands", ".md"),
    };

    /// <summary>
    /// Loads the skills declared in the active workspace, from the folder-per-skill convention
    /// (<c>skills/*/SKILL.md</c>, <c>.github/skills/*/SKILL.md</c>, <c>.claude/skills/*/SKILL.md</c>) and the
    /// flat-file convention (<c>.github/prompts/*.prompt.md</c>, <c>.claude/commands/*.md</c>). Returns an
    /// empty list when no workspace is active or when the workspace declares none. De-duplicated by name.
    /// </summary>
    /// <returns>The discovered skills, ordered by name (case-insensitive).</returns>
    internal IReadOnlyList<SkillDefinition> Load()
    {
        var scope = WorkspaceScope.Current;
        if (scope is null)
        {
            return [];
        }

        var skills = new List<SkillDefinition>();

        // Folder-per-skill (a SKILL.md per sub-directory).
        foreach (var folder in SkillFolders)
        {
            var skillsRoot = scope.ResolvePath(folder);
            if (!Directory.Exists(skillsRoot))
            {
                continue;
            }

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
                skills.Add(new SkillDefinition(name, description, $"{folder}/{id}/{SkillFileName}"));
            }
        }

        // Flat, one-file-per-skill (prompt/command files): the name is the file name; the description is
        // the optional frontmatter's, and the full body is read on demand by ReadSkill.
        foreach (var (folder, suffix) in FlatSkillSources)
        {
            var skillsRoot = scope.ResolvePath(folder);
            if (!Directory.Exists(skillsRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(skillsRoot, "*" + suffix, SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                var name = fileName[..^suffix.Length];
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var (_, description) = ReadFrontmatter(file);
                skills.Add(new SkillDefinition(name, description, $"{folder}/{fileName}"));
            }
        }

        return skills
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The skills available to the model this run: <see cref="Load"/> minus any the caller disabled via
    /// the active <see cref="SkillFilterScope"/> (skills default to enabled). Used to build the catalogue
    /// and to resolve <c>ReadSkill</c>, so a disabled skill is neither offered nor loadable.
    /// </summary>
    /// <returns>The enabled skills, ordered by name (case-insensitive).</returns>
    internal IReadOnlyList<SkillDefinition> LoadAvailable()
    {
        var scope = SkillFilterScope.Current;
        var all = Load();
        return scope is null ? all : all.Where(s => !scope.IsDisabled(s.Name)).ToList();
    }

    /// <summary>
    /// Builds the <c>&lt;skills&gt;</c> context block listing each available skill's name and description,
    /// for injection into the agent's instructions for a single run. Returns <c>null</c> when the active
    /// workspace declares no skills, so callers can skip injection entirely.
    /// </summary>
    /// <returns>The context block, or <c>null</c> when there are no skills.</returns>
    public string? BuildContextBlock()
    {
        var skills = LoadAvailable();
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
