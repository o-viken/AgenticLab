using System.Text.Json.Serialization;

namespace AgenticLab.Examples.Windfarm.Process;

[JsonConverter(typeof(JsonStringEnumConverter<CaseStage>))]
internal enum CaseStage { Investigating, Draft, AwaitingApproval, WorkOrderCreated, Rejected, Blocked, Archived }

[JsonConverter(typeof(JsonStringEnumConverter<ReviewVerdict>))]
internal enum ReviewVerdict { Ready, Concern, InsufficientEvidence }

internal sealed record EvidenceSource(string Id, string System, DateTimeOffset ObservedAt, string Version = "v1", bool Synthetic = true);
internal sealed record Telemetry(EvidenceSource Source, string AssetId, double GearboxTemperatureC, double VibrationMmPerSecond, int SampleCount);
internal sealed record MaintenanceHistory(EvidenceSource Source, string AssetId, string Findings);
internal sealed record WeatherWindow(string Id, EvidenceSource Source, DateTimeOffset Start, DateTimeOffset End, double WindMetresPerSecond, decimal ExpectedOutputMw);
internal sealed record Crew(EvidenceSource Source, string Id, string[] Qualifications, DateTimeOffset AvailableFrom, DateTimeOffset AvailableUntil);
internal sealed record Part(EvidenceSource Source, string Code, int Quantity, DateTimeOffset AvailableFrom);
internal sealed record MaintenanceProcedure(EvidenceSource Source, string Id, string Task, string Qualification, string PartCode, int PartQuantity, double MaxWindMetresPerSecond, int DurationHours);
internal sealed record ScenarioSummary(string Id, string Name, string Alarm, string AssetId, DateTimeOffset ScenarioTime, bool Synthetic = true);
internal sealed record Scenario(ScenarioSummary Summary, Telemetry Telemetry, MaintenanceHistory History,
    WeatherWindow[] Windows, Crew[] Crews, Part[] Parts, MaintenanceProcedure Procedure)
{
    public IEnumerable<EvidenceSource> Sources => new[] { Telemetry.Source, History.Source, Procedure.Source }
        .Concat(Windows.Select(window => window.Source)).Concat(Crews.Select(crew => crew.Source)).Concat(Parts.Select(part => part.Source));
}

internal sealed record MaintenanceOption(string Id, string WindowId, DateTimeOffset Start, DateTimeOffset End,
    string CrewId, string PartCode, int PartQuantity, decimal EstimatedLostMwh, bool Eligible,
    IReadOnlyList<string> BlockingReasons, IReadOnlyList<string> SourceIds);
internal sealed record MaintenanceDraft(int Revision, string OptionId, string Task, string Rationale,
    MaintenanceOption Option, IReadOnlyList<string> SourceIds, string EvidenceVersion);
internal sealed record SpecialistReview(string AgentName, int Revision, ReviewVerdict Verdict,
    string Observations, IReadOnlyList<string> Concerns, IReadOnlyList<string> SourceIds, DateTimeOffset RecordedAt);
internal sealed record ReviewAnswer(ReviewVerdict Verdict, string Observations, string[] Concerns, string[] SourceIds);
internal sealed record WorkOrder(string Id, string AssetId, string Task, DateTimeOffset Start, DateTimeOffset End,
    string CrewId, string PartCode, int ReservedQuantity, int ApprovedRevision, string ProposalHash, DateTimeOffset CreatedAt,
    string Status = "Planned inspection (simulation)");
internal sealed record CaseAudit(int Sequence, DateTimeOffset At, string Action, int Revision, string Detail);
internal sealed record CaseSnapshot(string Id, string ConversationId, ScenarioSummary Scenario, CaseStage Stage,
    int Revision, MaintenanceDraft? Draft, IReadOnlyList<SpecialistReview> Reviews, string? ProposalHash,
    WorkOrder? Order, IReadOnlyList<CaseAudit> Audit, DateTimeOffset ExpiresAt);

internal sealed class WindfarmException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

internal sealed class WindfarmOptions
{
    public int MaxCases { get; set; } = 100;
    public TimeSpan InactiveTtl { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}