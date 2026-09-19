namespace AgenticLab.Web.Flow;

/// <summary>
/// The user's persisted workspace preferences — the base folders to browse for repos and the recently used
/// workspace paths — plus the repo sub-folders discovered under those bases and the merged suggestion list
/// the workspace picker offers. Raises <see cref="Changed"/> (separately from the page-wide change event)
/// when a persisted value changes so the page can save it without saving on every re-render.
/// </summary>
internal sealed class WorkspacePrefs(Action notify)
{
    private string _bases = string.Empty;
    private readonly List<string> _recent = new();
    private readonly List<WorkspaceEntry> _directories = new();

    /// <summary>Raised when a persisted preference (bases or recent paths) changes.</summary>
    public event Action? Changed;

    private void NotifyPrefs()
    {
        Changed?.Invoke();
        notify();
    }

    /// <summary>The base folders (one per line, or ';'-separated) whose repo sub-folders are suggested.</summary>
    public string Bases
    {
        get => _bases;
        set { _bases = value ?? string.Empty; NotifyPrefs(); }
    }

    /// <summary>The parsed, de-duplicated base folder paths.</summary>
    public IReadOnlyList<string> BasePaths =>
        _bases
            .Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The most recently used workspace paths, newest first.</summary>
    public IReadOnlyList<string> Recent => _recent;

    /// <summary>Replaces the discovered repo sub-folders (from the base folders) used for suggestions.</summary>
    public void SetDirectories(IEnumerable<WorkspaceEntry> directories)
    {
        _directories.Clear();
        _directories.AddRange(directories);
        notify();
    }

    /// <summary>Records a used workspace path at the top of the recent list (de-duped, capped at 8), and persists it.</summary>
    public void AddRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var trimmed = path.Trim();
        _recent.RemoveAll(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, trimmed);
        while (_recent.Count > 8)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }

        NotifyPrefs();
    }

    /// <summary>Restores the persisted preferences on load without re-triggering a save.</summary>
    public void Init(string? bases, IEnumerable<string>? recent)
    {
        _bases = bases ?? string.Empty;
        _recent.Clear();
        if (recent is not null)
        {
            _recent.AddRange(recent.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()));
        }

        notify();
    }

    /// <summary>
    /// The picker's suggestions: recent paths first, then the discovered repo sub-folders, de-duplicated by
    /// full path. Each carries the folder name so a long path prefix doesn't obscure which repo it is.
    /// </summary>
    public IReadOnlyList<WorkspaceSuggestion> Suggestions
    {
        get
        {
            var list = new List<WorkspaceSuggestion>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var recent in _recent)
            {
                if (seen.Add(recent))
                {
                    list.Add(new WorkspaceSuggestion(recent, FolderName(recent), IsRecent: true));
                }
            }

            foreach (var dir in _directories)
            {
                if (seen.Add(dir.Path))
                {
                    list.Add(new WorkspaceSuggestion(dir.Path, dir.Name, IsRecent: false));
                }
            }

            return list;
        }
    }

    private static string FolderName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
