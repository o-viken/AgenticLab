namespace AgenticLab.AiService.Endpoints;

/// <summary>Read-only endpoints that inspect a workspace: its skills, custom instructions and candidate repo folders.</summary>
internal static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        // Lists the skills discovered in a given workspace (names + descriptions) so a client can show the
        // skill catalogue before a run starts. Returns an empty list when the path is missing/invalid or the
        // workspace declares no skills.
        app.MapPost("/skills", (SkillsRequest request, SkillLoader skills) =>
        {
            using var workspace = WorkspaceScope.TryBegin(request.Workspace);
            if (workspace is null)
            {
                return Results.Ok(new SkillsResponse(Array.Empty<SkillInfo>()));
            }

            var discovered = skills.Load()
                .Select(s => new SkillInfo(s.Name, s.Description))
                .ToList();
            return Results.Ok(new SkillsResponse(discovered));
        });

        // Lists the custom instructions discovered in a given workspace (names + descriptions) so a client
        // can offer them per run. Returns an empty list when the path is missing/invalid or there are none.
        app.MapPost("/instructions", (InstructionsRequest request, InstructionLoader instructions) =>
        {
            using var workspace = WorkspaceScope.TryBegin(request.Workspace);
            if (workspace is null)
            {
                return Results.Ok(new InstructionsResponse(Array.Empty<InstructionInfo>()));
            }

            var discovered = instructions.Load()
                .Select(i => new InstructionInfo(i.Name, i.Description))
                .ToList();
            return Results.Ok(new InstructionsResponse(discovered));
        });

        // Lists the immediate sub-folders of one or more base folders so a client can suggest workspace paths
        // (e.g. the repo folders under a "GitHub" directory the user pointed at).
        app.MapPost("/workspaces", (WorkspaceBrowseRequest request) =>
            Results.Ok(new WorkspaceBrowseResponse(BrowseWorkspaces(request.Bases))));

        return app;
    }

    // A read-only directory listing that skips blank/non-existent bases and hidden/system sub-folders,
    // dedups by full path, and caps the result so a huge base folder can't flood the client.
    private static IReadOnlyList<WorkspaceEntry> BrowseWorkspaces(IReadOnlyList<string>? bases)
    {
        if (bases is not { Count: > 0 })
        {
            return Array.Empty<WorkspaceEntry>();
        }

        const int maxEntries = 300;
        var entries = new List<WorkspaceEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in bases)
        {
            if (entries.Count >= maxEntries || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim());
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(full))
            {
                continue;
            }

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(full).OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // A base we can't read (permissions) simply contributes nothing.
                continue;
            }

            foreach (var dir in subdirs)
            {
                if (entries.Count >= maxEntries)
                {
                    break;
                }

                try
                {
                    var attrs = File.GetAttributes(dir);
                    if (attrs.HasFlag(FileAttributes.Hidden) || attrs.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (seen.Add(dir))
                {
                    entries.Add(new WorkspaceEntry(dir, Path.GetFileName(dir), full));
                }
            }
        }

        return entries;
    }
}

internal sealed record SkillsRequest(string? Workspace);
internal sealed record SkillsResponse(IReadOnlyList<SkillInfo> Skills);
internal sealed record SkillInfo(string Name, string Description);
internal sealed record InstructionsRequest(string? Workspace);
internal sealed record InstructionsResponse(IReadOnlyList<InstructionInfo> Instructions);
internal sealed record InstructionInfo(string Name, string Description);
internal sealed record WorkspaceBrowseRequest(IReadOnlyList<string>? Bases);
internal sealed record WorkspaceBrowseResponse(IReadOnlyList<WorkspaceEntry> Directories);
internal sealed record WorkspaceEntry(string Path, string Name, string Base);
