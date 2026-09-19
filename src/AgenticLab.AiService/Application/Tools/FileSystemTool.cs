using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Tools;

/// <summary>
/// File-system capabilities exposed to the coding agent: reading, listing, writing (create/update)
/// and deleting files. Every path is resolved through the active <see cref="WorkspaceScope"/>, which
/// confines access to the user's chosen workspace folder and rejects path-traversal escapes.
/// </summary>
public sealed class FileSystemTool
{
    private const int MaxReadChars = 20_000;

    /// <summary>Reads a text file from the workspace and returns its contents.</summary>
    /// <param name="path">The workspace-relative path to the file, e.g. <c>src/app.cs</c>.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The file contents, or a message when the file is missing.</returns>
    [Description("Read a text file from the workspace and return its contents.")]
    public async Task<string> ReadFile(
        [Description("The workspace-relative path to the file, e.g. 'src/Program.cs'.")] string path,
        CancellationToken cancellationToken = default)
    {
        var full = WorkspaceScope.Require().ResolvePath(path);
        if (!File.Exists(full))
        {
            return $"No file found at '{path}'.";
        }

        var text = await File.ReadAllTextAsync(full, cancellationToken);
        return text.Length > MaxReadChars
            ? text[..MaxReadChars] + $"\n… (truncated, {text.Length} characters total)"
            : text;
    }

    /// <summary>Lists files and directories under a workspace folder.</summary>
    /// <param name="path">The workspace-relative folder to list; empty or "." for the root.</param>
    /// <returns>A newline-separated listing, or a message when the folder is missing.</returns>
    [Description("List files and directories under a workspace folder. Use '.' or an empty string for the workspace root.")]
    public string ListFiles(
        [Description("The workspace-relative folder to list, e.g. 'src'. Use '.' for the root.")] string path = ".")
    {
        var relative = string.IsNullOrWhiteSpace(path) ? "." : path;
        var full = WorkspaceScope.Require().ResolvePath(relative);
        if (!Directory.Exists(full))
        {
            return $"No directory found at '{relative}'.";
        }

        var sb = new StringBuilder();
        foreach (var dir in Directory.EnumerateDirectories(full).OrderBy(p => p))
        {
            sb.AppendLine($"[dir]  {Path.GetFileName(dir)}/");
        }

        foreach (var file in Directory.EnumerateFiles(full).OrderBy(p => p))
        {
            sb.AppendLine($"[file] {Path.GetFileName(file)}");
        }

        return sb.Length == 0 ? $"'{relative}' is empty." : sb.ToString();
    }

    /// <summary>Creates a new file or overwrites an existing one with the given contents.</summary>
    /// <param name="path">The workspace-relative path to write to.</param>
    /// <param name="content">The full text content to write.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A confirmation message.</returns>
    [Description("Create a new file or overwrite an existing one with the given content. Parent directories are created automatically.")]
    public async Task<string> WriteFile(
        [Description("The workspace-relative path to write to, e.g. 'src/hello.txt'.")] string path,
        [Description("The full text content to write to the file.")] string content,
        CancellationToken cancellationToken = default)
    {
        var full = WorkspaceScope.Require().ResolvePath(path);
        var existed = File.Exists(full);

        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(full, content, cancellationToken);
        return existed ? $"Updated '{path}'." : $"Created '{path}'.";
    }

    /// <summary>Deletes a file from the workspace.</summary>
    /// <param name="path">The workspace-relative path to delete.</param>
    /// <returns>A confirmation message, or a note when the file did not exist.</returns>
    [Description("Delete a file from the workspace. Only files can be deleted, not directories.")]
    public string DeleteFile(
        [Description("The workspace-relative path to delete, e.g. 'src/hello.txt'.")] string path)
    {
        var full = WorkspaceScope.Require().ResolvePath(path);
        if (Directory.Exists(full))
        {
            return $"'{path}' is a directory; only files can be deleted.";
        }

        if (!File.Exists(full))
        {
            return $"No file found at '{path}'.";
        }

        File.Delete(full);
        return $"Deleted '{path}'.";
    }

    /// <summary>Exposes this tool's methods as AI tools for an agent.</summary>
    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(ReadFile),
        AIFunctionFactory.Create(ListFiles),
        AIFunctionFactory.Create(WriteFile),
        AIFunctionFactory.Create(DeleteFile),
    ];

    /// <summary>
    /// Exposes only the read-only subset of this tool (<see cref="ReadFile"/> and
    /// <see cref="ListFiles"/>), for agents that may inspect the workspace but must not change it.
    /// </summary>
    public IList<AITool> AsReadOnlyTools() =>
    [
        AIFunctionFactory.Create(ReadFile),
        AIFunctionFactory.Create(ListFiles),
    ];
}
