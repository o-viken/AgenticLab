namespace TheSeries.AiService.Agents;

/// <summary>
/// An ambient, per-run scope that carries the validated <em>workspace root</em> the file-system and
/// terminal tools operate against. Like <see cref="FlowCaptureScope"/> it lives in an
/// <see cref="AsyncLocal{T}"/> so concurrent runs stay isolated and the shared singleton tools only
/// touch the workspace of the request that is currently executing.
/// </summary>
/// <remarks>
/// All path access goes through <see cref="ResolvePath"/>, which confines every path to the workspace
/// root and rejects attempts to escape it (e.g. <c>..</c> segments or absolute paths) — the single
/// security guard that keeps the tools from reading or writing outside the user's chosen folder.
/// </remarks>
public sealed class WorkspaceScope : IDisposable
{
    private static readonly AsyncLocal<WorkspaceScope?> CurrentScope = new();

    private WorkspaceScope(string root) => Root = root;

    /// <summary>The scope active on the current async context, or null when no workspace is set.</summary>
    public static WorkspaceScope? Current => CurrentScope.Value;

    /// <summary>The absolute, normalized workspace root every tool path is resolved against.</summary>
    public string Root { get; }

    /// <summary>
    /// Starts a new workspace scope on the current async context. Dispose to end it.
    /// </summary>
    /// <param name="root">The user-supplied workspace path; must be an existing directory.</param>
    /// <returns>The newly started scope.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="root"/> is blank.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist.</exception>
    public static WorkspaceScope Begin(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("A workspace path is required.", nameof(root));
        }

        var full = Path.GetFullPath(root.Trim());
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"Workspace directory does not exist: {full}");
        }

        var scope = new WorkspaceScope(full);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Re-asserts this scope as the active one on the current async context. The value lives in an
    /// <see cref="AsyncLocal{T}"/>, which is reset whenever an owning async iterator resumes after a
    /// <c>yield return</c>; callers driving the agent through such an iterator must re-activate before
    /// each advance so the tools see the workspace on every LLM round-trip.
    /// </summary>
    public void Activate() => CurrentScope.Value = this;

    /// <summary>The scope active on the current context, or a thrown error when none is set.</summary>
    /// <exception cref="InvalidOperationException">Thrown when no workspace scope is active.</exception>
    public static WorkspaceScope Require() =>
        Current ?? throw new InvalidOperationException(
            "No workspace is set for this run. The selected agent requires a workspace path.");

    /// <summary>
    /// Resolves a workspace-relative (or absolute) path to a full path and verifies it stays inside the
    /// workspace root, guarding against path-traversal escapes.
    /// </summary>
    /// <param name="relativePath">The path to resolve, relative to the workspace root.</param>
    /// <returns>The confined, absolute path.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is blank.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the path escapes the workspace root.</exception>
    public string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A path is required.", nameof(relativePath));
        }

        var combined = Path.GetFullPath(Path.Combine(Root, relativePath.Trim()));
        var rootWithSep = Root.EndsWith(Path.DirectorySeparatorChar)
            ? Root
            : Root + Path.DirectorySeparatorChar;

        if (!string.Equals(combined, Root, StringComparison.OrdinalIgnoreCase) &&
            !combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Path '{relativePath}' resolves outside the workspace and is not allowed.");
        }

        return combined;
    }

    /// <summary>Ends the scope, clearing it from the current async context.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            CurrentScope.Value = null;
        }
    }
}
