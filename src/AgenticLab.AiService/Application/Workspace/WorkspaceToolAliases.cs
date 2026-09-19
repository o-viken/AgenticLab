namespace AgenticLab.AiService.Application.Workspace;

/// <summary>
/// Maps the tool tokens declared in VS Code / GitHub Copilot / Claude Code custom-agent files (optionally
/// namespaced, e.g. <c>search/fileSearch</c>) to this app's backend tool names. Only the segment after the
/// last <c>/</c> is matched, case-insensitively. The table is intentionally small and meant to be extended
/// as more tokens are encountered; a token with no known mapping is dropped rather than granting anything
/// in its place.
/// </summary>
internal static class WorkspaceToolAliases
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
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
    /// Resolves declared tokens to backend tool names. Returns the de-duplicated resolved tools plus one
    /// <see cref="ToolMapping"/> per declared token (a null target marks a dropped token), so callers can
    /// surface the mapping.
    /// </summary>
    internal static (IReadOnlyList<string> Tools, IReadOnlyList<ToolMapping> Mappings) Map(IEnumerable<string> tokens)
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

            if (Aliases.TryGetValue(key, out var backendName))
            {
                mappings.Add(new ToolMapping(declared, backendName));
                if (!mapped.Contains(backendName, StringComparer.OrdinalIgnoreCase))
                {
                    mapped.Add(backendName);
                }
            }
            else
            {
                mappings.Add(new ToolMapping(declared, null));
            }
        }

        return (mapped, mappings);
    }
}
