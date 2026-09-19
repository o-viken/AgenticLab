using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Tools;

/// <summary>
/// Runs shell commands inside the active <see cref="WorkspaceScope"/> on the coding agent's behalf.
/// Commands are executed through the OS shell (<c>cmd.exe</c> on Windows, <c>/bin/sh</c> elsewhere) so
/// built-ins (e.g. <c>dir</c>, <c>ls</c>) and script-based tools (e.g. <c>npm.cmd</c>) resolve just as
/// they would in a terminal. For safety the executable must be on an <em>allowlist</em> of known-safe
/// programs and the arguments may not contain shell operators (so the allowlist can't be bypassed by
/// chaining commands). Commands run with the workspace root as their working directory, output is
/// captured, and a timeout prevents a stuck process from hanging the run.
/// </summary>
public sealed class TerminalTool
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    // Shell control characters rejected in arguments so a single allowlisted command can't chain into others.
    private static readonly char[] ShellOperators = ['&', '|', ';', '<', '>', '`', '$', '\n', '\r'];

    private readonly HashSet<string> _allowlist;

    /// <summary>
    /// Creates the tool with an allowlist of permitted executables. Falls back to a sensible default
    /// set (<c>dotnet, git, ls, dir, npm, node, python, pip, powershell, pwsh</c>) when configuration
    /// supplies none.
    /// </summary>
    /// <param name="configuration">Configuration optionally providing <c>Coder:AllowedCommands</c>.</param>
    public TerminalTool(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Coder:AllowedCommands").Get<string[]>();
        var commands = configured is { Length: > 0 }
            ? configured
            : ["dotnet", "git", "ls", "dir", "npm", "node", "python", "pip", "powershell", "pwsh"];
        _allowlist = new HashSet<string>(commands, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A human-readable description of the runtime environment for the agent's system prompt: the OS,
    /// the shell commands run through, and the allowlisted commands. Helps the agent choose the right
    /// command for the platform (e.g. <c>dir</c> on Windows vs <c>ls</c> on Unix).
    /// </summary>
    public string EnvironmentInfo
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux/Unix";
            var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
            var listExample = OperatingSystem.IsWindows() ? "dir" : "ls";
            return
                $"You are running on {os} ({RuntimeInformation.OSDescription.Trim()}). " +
                $"Terminal commands run through {shell} with the workspace folder as the working directory. " +
                $"Use commands appropriate for this OS — for example, list files with '{listExample}'. " +
                "Run a single command per call; shell operators and command chaining (& | ; < > etc.) are not allowed. " +
                $"Allowed commands: {string.Join(", ", _allowlist.OrderBy(c => c))}.";
        }
    }

    /// <summary>Runs an allowlisted command in the workspace and returns its output.</summary>
    /// <param name="command">The executable to run, e.g. <c>dotnet</c>. Must be on the allowlist.</param>
    /// <param name="arguments">The arguments to pass, e.g. <c>build</c>. May be empty.</param>
    /// <param name="cancellationToken">A token to cancel the command.</param>
    /// <returns>The combined stdout/stderr and exit code, or a message when the command is not allowed.</returns>
    [Description("Run a shell command in the workspace and return its output. The command must be on the allowlist of safe executables.")]
    public async Task<string> RunCommand(
        [Description("The executable to run, e.g. 'dotnet' or 'git'. Only allowlisted commands are permitted.")] string command,
        [Description("The arguments to pass to the command, e.g. 'build' or 'status'. Leave empty for none.")] string arguments = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "No command was provided.";
        }

        var exe = command.Trim();
        if (!_allowlist.Contains(exe))
        {
            return $"Command '{exe}' is not allowed. Permitted commands: {string.Join(", ", _allowlist.OrderBy(c => c))}.";
        }

        var args = arguments?.Trim() ?? string.Empty;
        if (args.IndexOfAny(ShellOperators) >= 0)
        {
            return "The arguments contain shell control characters (& | ; < > ` $ or a newline), which are not allowed. Run one command per call.";
        }

        var workspace = WorkspaceScope.Require().Root;
        var commandLine = string.IsNullOrEmpty(args) ? exe : $"{exe} {args}";

        // Run through the OS shell so built-ins (dir/ls) and script-based commands (e.g. npm.cmd) resolve
        // the same way they would in a terminal, instead of requiring an exact executable path.
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/c {commandLine}";
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(commandLine);
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return $"Failed to start '{commandLine}': {ex.Message}";
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return $"Command '{commandLine}' timed out after {Timeout.TotalSeconds:N0}s and was terminated.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"$ {commandLine}");
        sb.AppendLine($"Exit code: {process.ExitCode}");
        if (stdout.Length > 0)
        {
            sb.AppendLine("stdout:").Append(stdout);
        }

        if (stderr.Length > 0)
        {
            sb.AppendLine("stderr:").Append(stderr);
        }

        return sb.ToString();
    }

    /// <summary>Exposes this tool's methods as AI tools for an agent.</summary>
    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(RunCommand),
    ];

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort; the process may already be gone.
        }
    }
}
