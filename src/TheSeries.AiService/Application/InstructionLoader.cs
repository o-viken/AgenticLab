namespace TheSeries.AiService.Application;

/// <summary>
/// Discovers the custom instructions available in the active <see cref="WorkspaceScope"/>, modelled on
/// GitHub Copilot's custom instructions. Instructions live under a top-level <c>instructions/</c> folder,
/// one per <c>*.instructions.md</c> file, with an optional YAML frontmatter <c>description</c> (the
/// <c>name</c> defaults to the file name):
/// <code>
/// ---
/// description: Project coding conventions the agent must follow.
/// ---
/// (markdown body with the instructions the agent should always follow)
/// </code>
/// Unlike skills (a catalogue injected up front, full body loaded on demand via a tool), custom
/// instructions are <em>always applied</em>: their full body is injected into the agent's context on
/// every run via <see cref="BuildContextBlock"/>. All paths are resolved through the workspace scope, so
/// discovery stays confined to the workspace folder. Registered as a singleton; it reads the ambient
/// workspace scope on each call and never caches.
/// </summary>
public sealed class InstructionLoader
{
    private const string FileSuffix = ".instructions.md";

    // The folders scanned for the `*.instructions.md` convention (a top-level `instructions/` folder plus
    // the GitHub Copilot `.github/instructions/` folder).
    private static readonly string[] InstructionFolders = { "instructions", ".github/instructions" };

    // Whole-file instruction conventions: a single named file whose entire body is the instruction. The
    // name and a friendly fallback description are supplied here (ReadFile treats a file without
    // frontmatter as all-body). Covers GitHub Copilot's repo-wide file, Claude Code's CLAUDE.md and the
    // AGENTS.md convention.
    private static readonly (string Path, string Name, string Description)[] WholeFileSources =
    {
        (".github/copilot-instructions.md", "copilot-instructions", "Repository-wide GitHub Copilot instructions"),
        ("CLAUDE.md", "CLAUDE", "Claude Code project instructions (CLAUDE.md)"),
        ("AGENTS.md", "AGENTS", "Repository AGENTS.md instructions"),
    };

    /// <summary>
    /// Loads the custom instructions declared in the active workspace, from the <c>*.instructions.md</c>
    /// folders (<c>instructions/</c> and <c>.github/instructions/</c>) and the whole-file conventions
    /// (<c>.github/copilot-instructions.md</c>, <c>CLAUDE.md</c>, <c>AGENTS.md</c>). Returns an empty list
    /// when no workspace is active or when the workspace declares none. De-duplicated by name.
    /// </summary>
    /// <returns>The discovered instructions, ordered by name (case-insensitive).</returns>
    internal IReadOnlyList<InstructionDefinition> Load()
    {
        var scope = WorkspaceScope.Current;
        if (scope is null)
        {
            return [];
        }

        var instructions = new List<InstructionDefinition>();

        // *.instructions.md files across the configured folders.
        foreach (var folder in InstructionFolders)
        {
            var root = scope.ResolvePath(folder);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, $"*{FileSuffix}", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                var defaultName = fileName[..^FileSuffix.Length];
                var (name, description, body) = ReadFile(file, defaultName);
                if (string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                instructions.Add(new InstructionDefinition(name, description, body, $"{folder}/{fileName}"));
            }
        }

        // Whole-file instruction conventions (copilot-instructions.md, CLAUDE.md, AGENTS.md).
        foreach (var (path, name, fallbackDescription) in WholeFileSources)
        {
            var file = scope.ResolvePath(path);
            if (!File.Exists(file))
            {
                continue;
            }

            var (_, fileDescription, body) = ReadFile(file, name);
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var description = string.IsNullOrWhiteSpace(fileDescription) ? fallbackDescription : fileDescription;
            instructions.Add(new InstructionDefinition(name, description, body, path));
        }

        return instructions
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds the <c>&lt;customInstructions&gt;</c> context block containing the full body of every custom
    /// instruction the caller <em>opted in</em> for this run, for injection into the agent's instructions.
    /// Custom instructions default to off, so only instructions enabled via the active
    /// <see cref="InstructionFilterScope"/> are included; returns <c>null</c> when none are enabled (or no
    /// scope is active), so callers can skip injection entirely.
    /// </summary>
    /// <returns>The context block, or <c>null</c> when no instruction is enabled.</returns>
    public string? BuildContextBlock()
    {
        var scope = InstructionFilterScope.Current;
        var instructions = scope is null
            ? []
            : Load().Where(i => scope.IsEnabled(i.Name)).ToList();
        if (instructions.Count == 0)
        {
            return null;
        }

        var sections = instructions.Select(i =>
        {
            var heading = string.IsNullOrWhiteSpace(i.Description)
                ? $"## {i.Name}"
                : $"## {i.Name} — {i.Description}";
            return $"{heading}\n{i.Body.Trim()}";
        });

        return
            "<customInstructions>\n" +
            "The following project-specific custom instructions apply to this workspace. Always follow " +
            "them in addition to your other rules; when they conflict with a general preference, prefer " +
            "these instructions.\n\n" +
            string.Join("\n\n", sections) +
            "\n</customInstructions>";
    }

    // Reads a custom instruction file: parses the optional leading YAML frontmatter (the block delimited
    // by '---' lines) for 'name' and 'description', and returns the remaining markdown as the body. The
    // parser is intentionally minimal — no YAML dependency — since the frontmatter is a flat set of
    // simple key: value pairs. The name falls back to the supplied default (the file name) when absent.
    private static (string Name, string Description, string Body) ReadFile(string file, string defaultName)
    {
        var lines = File.ReadAllLines(file);
        var name = defaultName;
        var description = string.Empty;

        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            // No frontmatter: the whole file is the instruction body.
            return (name, description, string.Join('\n', lines));
        }

        var bodyStart = lines.Length;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == "---")
            {
                bodyStart = i + 1;
                break;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                name = value;
            }
            else if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                description = value;
            }
        }

        var body = bodyStart >= lines.Length ? string.Empty : string.Join('\n', lines[bodyStart..]);
        return (name, description, body);
    }
}
