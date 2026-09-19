namespace AgenticLab.AiService.Application.Flow;

/// <summary>An execution-boundary pause or release, identified uniquely within a flow run.</summary>
public sealed record BreakpointNotice(string Id, string Kind, string? Tool, bool Paused, bool Manual);
