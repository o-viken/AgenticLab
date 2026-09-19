namespace AgenticLab.AiService.Application.Discovery;

/// <summary>A point-in-time snapshot of every discovery source's status plus the startup-discovery flag.</summary>
/// <param name="DiscoverOnStartup">Whether discovery runs automatically at startup.</param>
/// <param name="Sources">The per-source discovery statuses.</param>
public sealed record DiscoverySnapshot(bool DiscoverOnStartup, IReadOnlyList<DiscoverySourceStatus> Sources);
