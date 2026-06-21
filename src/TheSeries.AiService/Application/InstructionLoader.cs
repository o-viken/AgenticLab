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
    private const string InstructionsFolder = "instructions";
    private const string FileSuffix = ".instructions.md";

    /// <summary>
    /// Loads the custom instructions declared in the active workspace. Returns an empty list when no
    /// workspace is active, when the workspace has no <c>instructions/</c> folder, or when it contains no
    /// <c>*.instructions.md</c> files.
    /// </summary>
    /// <returns>The discovered instructions, ordered by name (case-insensitive).</returns>
    internal IReadOnlyList<InstructionDefinition> Load()
    {
        var scope = WorkspaceScope.Current;
        if (scope is null)
        {
            return [];
        }

        var root = scope.ResolvePath(InstructionsFolder);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var instructions = new List<InstructionDefinition>();
        foreach (var file in Directory.EnumerateFiles(root, $"*{FileSuffix}", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            var defaultName = fileName[..^FileSuffix.Length];
            var (name, description, body) = ReadFile(file, defaultName);
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            instructions.Add(new InstructionDefinition(name, description, body, $"{InstructionsFolder}/{fileName}"));
        }

        return instructions.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Builds the <c>&lt;customInstructions&gt;</c> context block containing the full body of every custom
    /// instruction in the active workspace, for injection into the agent's instructions for a single run.
    /// Returns <c>null</c> when the active workspace declares none, so callers can skip injection entirely.
    /// </summary>
    /// <returns>The context block, or <c>null</c> when there are no custom instructions.</returns>
    public string? BuildContextBlock()
    {
        var instructions = Load();
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
