using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TheSeries.McpServer.Tools;

/// <summary>
/// A super-simple MCP tool set exposing the local time, demonstrating real MCP discovery.
/// </summary>
[McpServerToolType]
public sealed class TimeTools
{
    /// <summary>Returns the current local date and time on the server.</summary>
    [McpServerTool(Name = "GetCurrentTime")]
    [Description("Get the current local date and time on the server.")]
    public static string GetCurrentTime() =>
        DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
}
