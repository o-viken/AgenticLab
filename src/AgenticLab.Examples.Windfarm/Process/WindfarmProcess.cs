using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgenticLab.Examples.Windfarm.Data;
using Microsoft.Extensions.Options;

namespace AgenticLab.Examples.Windfarm.Process;

internal interface IWindfarmProcess
{
    CaseSnapshot Create(string conversationId, string scenarioId);
    CaseSnapshot? Find(string conversationId);
    CaseSnapshot Get(string conversationId, string caseId);
    IReadOnlyList<MaintenanceOption> Options(string conversationId, string? expectedCaseId = null);
    CaseSnapshot Draft(string conversationId, string optionId, string rationale, IReadOnlyList<string> sourceIds, string? expectedCaseId = null);
    CaseSnapshot RecordReview(string conversationId, string caseId, string agentName, int revision, ReviewAnswer answer);
    CaseSnapshot Submit(string conversationId, string? expectedCaseId = null);
    CaseSnapshot Decide(string conversationId, string caseId, int revision, string hash, string decision, string idempotencyKey, string? reason);
    CaseSnapshot Archive(string conversationId, string caseId);
    int Cleanup();
}

internal sealed class WindfarmProcess(TimeProvider clock, IOptions<WindfarmOptions> options) : IWindfarmProcess
{
    internal static readonly string[] Reviewers = ["windfarm-reliability", "windfarm-planning", "windfarm-risk"];
    private readonly object _gate = new();
    private readonly Dictionary<string, CaseState> _cases = new(StringComparer.Ordinal);
    private readonly WindfarmOptions _options = options.Value;

    private sealed class CaseState(string id, string conversation, Scenario scenario, DateTimeOffset accessed)
    {
        public string Id { get; } = id;
        public string Conversation { get; } = conversation;
        public Scenario Scenario { get; } = scenario;
        public DateTimeOffset Accessed { get; set; } = accessed;
        public CaseStage Stage { get; set; } = CaseStage.Investigating;
        public int Revision { get; set; }
        public MaintenanceDraft? Draft { get; set; }
        public List<SpecialistReview> Reviews { get; } = [];
        public string? ProposalHash { get; set; }
        public WorkOrder? Order { get; set; }
        public List<CaseAudit> Audit { get; } = [];
        public int AuditSequence { get; set; }
        public Dictionary<string, (string Fingerprint, CaseSnapshot Result)> Decisions { get; } = new(StringComparer.Ordinal);
    }

    public CaseSnapshot Create(string conversationId, string scenarioId)
    {
        ValidateText(conversationId, 100, "Conversation ID");
        var scenario = WindfarmFixtures.Get(scenarioId);
        lock (_gate)
        {
            CleanupLocked();
            if (_cases.Values.Any(state => state.Conversation == conversationId && state.Stage != CaseStage.Archived))
                throw new WindfarmException(409, "Archive the current case before starting another.");
            if (_cases.Count >= _options.MaxCases) throw new WindfarmException(503, "The sandbox is at capacity; wait for inactive cases to expire.");
            var state = new CaseState(Guid.NewGuid().ToString("n"), conversationId, scenario, clock.GetUtcNow());
            if (!Evaluate(scenario).Any(candidate => candidate.Eligible)) state.Stage = CaseStage.Blocked;
            _cases.Add(state.Id, state);
            Audit(state, "Opened", "Synthetic Fjordvik Wind Farm case opened.");
            return Snapshot(state);
        }
    }

    public CaseSnapshot? Find(string conversationId)
    {
        lock (_gate)
        {
            CleanupLocked();
            var state = _cases.Values.FirstOrDefault(item => item.Conversation == conversationId && item.Stage != CaseStage.Archived);
            if (state is null) return null;
            state.Accessed = clock.GetUtcNow();
            return Snapshot(state);
        }
    }

    public CaseSnapshot Get(string conversationId, string caseId)
    {
        lock (_gate) return Snapshot(Require(conversationId, caseId));
    }

    public IReadOnlyList<MaintenanceOption> Options(string conversationId, string? expectedCaseId = null)
    {
        lock (_gate) return Evaluate(Active(conversationId, expectedCaseId).Scenario);
    }

    public CaseSnapshot Draft(string conversationId, string optionId, string rationale, IReadOnlyList<string> sourceIds, string? expectedCaseId = null)
    {
        ValidateText(rationale, 3000, "Rationale");
        lock (_gate)
        {
            var state = Active(conversationId, expectedCaseId);
            if (state.Stage is not (CaseStage.Investigating or CaseStage.Draft or CaseStage.Rejected))
                throw new WindfarmException(409, "This case cannot be edited. Pending approval is immutable.");
            if (state.Revision >= 32) throw new WindfarmException(409, "Draft limit reached; start a new case.");
            var candidate = Evaluate(state.Scenario).FirstOrDefault(item => item.Id == optionId)
                ?? throw new WindfarmException(400, "Unknown maintenance option.");
            if (!candidate.Eligible) throw new WindfarmException(409, string.Join(" ", candidate.BlockingReasons));
            ValidateSources(state.Scenario, sourceIds);
            if (candidate.SourceIds.Except(sourceIds, StringComparer.Ordinal).Any())
                throw new WindfarmException(409, "The draft must cite all evidence used by the option.");
            state.Revision++;
            state.Draft = new(state.Revision, candidate.Id, state.Scenario.Procedure.Task, rationale.Trim(), candidate,
                sourceIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), Hash(state.Scenario));
            state.Reviews.Clear();
            state.ProposalHash = null;
            state.Stage = CaseStage.Draft;
            Audit(state, "Drafted", $"Option {candidate.Id}; earlier reviews invalidated.");
            return Snapshot(state);
        }
    }

    public CaseSnapshot RecordReview(string conversationId, string caseId, string agentName, int revision, ReviewAnswer answer)
    {
        lock (_gate)
        {
            var state = Require(conversationId, caseId);
            if (state.Stage != CaseStage.Draft || state.Revision != revision)
                throw new WindfarmException(409, "The specialist reviewed a superseded draft.");
            if (!Reviewers.Contains(agentName, StringComparer.Ordinal)) throw new WindfarmException(400, "Unknown specialist.");
            ValidateText(answer.Observations, 4000, "Review observations");
            if (!Enum.IsDefined(answer.Verdict) || answer.Concerns is null || answer.Concerns.Length > 12
                || answer.Concerns.Any(concern => string.IsNullOrWhiteSpace(concern) || concern.Length > 1000))
                throw new WindfarmException(400, "Invalid review verdict or concerns.");
            ValidateSources(state.Scenario, answer.SourceIds);
            if (answer.SourceIds.Length == 0 || answer.SourceIds.Except(state.Draft!.SourceIds, StringComparer.Ordinal).Any())
                throw new WindfarmException(400, "Review sources must belong to this draft's evidence packet.");
            if (answer.Verdict == ReviewVerdict.Ready && answer.Concerns.Length > 0)
                throw new WindfarmException(409, "A review with unresolved concerns cannot be ready.");
            state.Reviews.RemoveAll(review => review.AgentName == agentName);
            state.Reviews.Add(new(agentName, revision, answer.Verdict, answer.Observations.Trim(),
                answer.Concerns.ToArray(), answer.SourceIds.Distinct().ToArray(), clock.GetUtcNow()));
            Audit(state, "Reviewed", $"{agentName}: {answer.Verdict}");
            return Snapshot(state);
        }
    }

    public CaseSnapshot Submit(string conversationId, string? expectedCaseId = null)
    {
        lock (_gate)
        {
            var state = Active(conversationId, expectedCaseId);
            if (state.Stage == CaseStage.AwaitingApproval) return Snapshot(state);
            if (state.Stage != CaseStage.Draft || state.Draft is null)
                throw new WindfarmException(409, "A current draft is required.");
            if (Reviewers.Any(name => !state.Reviews.Any(review => review.AgentName == name
                && review.Revision == state.Revision && review.Verdict == ReviewVerdict.Ready)))
                throw new WindfarmException(409, "All three specialists must return ready reviews for this exact draft.");
            CheckFeasible(state);
            state.ProposalHash = Hash(new { state.Id, state.Draft, Reviews = state.Reviews.OrderBy(review => review.AgentName).ToArray() });
            state.Stage = CaseStage.AwaitingApproval;
            Audit(state, "Submitted", "Proposal frozen for an explicit human decision. No work order exists yet.");
            return Snapshot(state);
        }
    }

    public CaseSnapshot Decide(string conversationId, string caseId, int revision, string hash, string decision, string idempotencyKey, string? reason)
    {
        ValidateText(idempotencyKey, 100, "Idempotency key");
        if (decision is not ("approve" or "reject")) throw new WindfarmException(400, "Decision must be approve or reject.");
        if (reason?.Length > 2000) throw new WindfarmException(400, "Decision reason is too long.");
        var fingerprint = Hash(new { conversationId, caseId, revision, hash, decision, reason });
        lock (_gate)
        {
            var state = Require(conversationId, caseId);
            if (state.Stage == CaseStage.Archived) throw new WindfarmException(409, "This case is archived.");
            if (state.Decisions.TryGetValue(idempotencyKey, out var previous))
                return previous.Fingerprint == fingerprint ? previous.Result : throw new WindfarmException(409, "Idempotency key already used with a different decision.");
            if (state.Stage != CaseStage.AwaitingApproval || revision != state.Revision
                || string.IsNullOrWhiteSpace(hash) || hash != state.ProposalHash)
                throw new WindfarmException(409, "This exact proposal is no longer awaiting approval. Refresh the case.");
            CheckFeasible(state);
            if (decision == "approve")
            {
                var draft = state.Draft!;
                var option = draft.Option;
                state.Order = new($"WO-{state.Id[..8]}-{revision}", state.Scenario.Summary.AssetId, draft.Task,
                    option.Start, option.End, option.CrewId, option.PartCode, option.PartQuantity, revision, hash, clock.GetUtcNow());
                state.Stage = CaseStage.WorkOrderCreated;
                Audit(state, "WorkOrderCreated", $"Human approved revision {revision}; {state.Order.Id} reserves this case's crew and parts only.");
            }
            else
            {
                state.Stage = CaseStage.Rejected;
                Audit(state, "Rejected", reason?.Trim() is { Length: > 0 } detail ? detail : "Human rejected the proposal.");
            }
            var result = Snapshot(state);
            state.Decisions.Add(idempotencyKey, (fingerprint, result));
            return result;
        }
    }

    public CaseSnapshot Archive(string conversationId, string caseId)
    {
        lock (_gate)
        {
            var state = Require(conversationId, caseId);
            if (state.Stage != CaseStage.Archived)
            {
                state.Stage = CaseStage.Archived;
                Audit(state, "Archived", "Pending decisions invalidated; an existing simulated order is retained in history.");
            }
            return Snapshot(state);
        }
    }

    public int Cleanup()
    {
        lock (_gate) return CleanupLocked();
    }

    private int CleanupLocked()
    {
        var expired = _cases.Values.Where(state => clock.GetUtcNow() - state.Accessed >= _options.InactiveTtl).Select(state => state.Id).ToArray();
        foreach (var id in expired) _cases.Remove(id);
        return expired.Length;
    }

    private CaseState Require(string conversationId, string caseId)
    {
        CleanupLocked();
        if (!_cases.TryGetValue(caseId, out var state) || state.Conversation != conversationId)
            throw new WindfarmException(404, "Case unavailable, expired, or belongs to another conversation. Start a new case explicitly.");
        state.Accessed = clock.GetUtcNow();
        return state;
    }

    private CaseState Active(string conversationId, string? expectedCaseId = null)
    {
        CleanupLocked();
        var state = _cases.Values.FirstOrDefault(item => item.Conversation == conversationId && item.Stage != CaseStage.Archived)
            ?? throw new WindfarmException(409, "Start a scenario in the example panel before asking the coordinator to act.");
        if (expectedCaseId is not null && state.Id != expectedCaseId)
            throw new WindfarmException(409, "The addressed case is no longer active for this conversation.");
        state.Accessed = clock.GetUtcNow();
        return state;
    }

    private void Audit(CaseState state, string action, string detail)
    {
        state.Audit.Add(new(++state.AuditSequence, clock.GetUtcNow(), action, state.Revision, detail));
        if (state.Audit.Count > 128) state.Audit.RemoveAt(0);
    }

    private CaseSnapshot Snapshot(CaseState state) => new(state.Id, state.Conversation, state.Scenario.Summary,
        state.Stage, state.Revision, state.Draft, state.Reviews.ToArray(), state.ProposalHash, state.Order,
        state.Audit.ToArray(), state.Accessed + _options.InactiveTtl);

    private static void ValidateText(string? value, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw new WindfarmException(400, $"{name} is required and must be at most {maximum} characters.");
    }

    private static void ValidateSources(Scenario scenario, IReadOnlyList<string>? sources)
    {
        if (sources is null || sources.Count > 20 || sources.Except(scenario.Sources.Select(source => source.Id), StringComparer.Ordinal).Any())
            throw new WindfarmException(400, "Evidence references must belong to this scenario snapshot.");
    }

    private static void CheckFeasible(CaseState state)
    {
        if (state.Draft is null || state.Draft.EvidenceVersion != Hash(state.Scenario)
            || !Evaluate(state.Scenario).Any(option => option.Id == state.Draft.OptionId && option.Eligible))
            throw new WindfarmException(409, "Evidence or resource constraints no longer support the draft.");
    }

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private static IReadOnlyList<MaintenanceOption> Evaluate(Scenario scenario)
    {
        var result = new List<MaintenanceOption>();
        var procedure = scenario.Procedure;
        foreach (var window in scenario.Windows)
        foreach (var crew in scenario.Crews)
        {
            var reasons = new List<string>();
            var end = window.Start.AddHours(procedure.DurationHours);
            var part = scenario.Parts.Single(item => item.Code == procedure.PartCode);
            if (scenario.Telemetry.SampleCount < 10 || scenario.Summary.ScenarioTime - scenario.Telemetry.Source.ObservedAt > TimeSpan.FromHours(2))
                reasons.Add("Telemetry evidence is stale or incomplete; obtain a current sample before planning.");
            if (window.WindMetresPerSecond > procedure.MaxWindMetresPerSecond) reasons.Add("Forecast exceeds the illustrative procedure's wind limit.");
            if (end > window.End) reasons.Add("The inspection does not fit within the weather window.");
            if (!crew.Qualifications.Contains(procedure.Qualification)) reasons.Add("Crew lacks the required inspection qualification.");
            if (crew.AvailableFrom > window.Start || crew.AvailableUntil < end) reasons.Add("Crew is unavailable for the full inspection.");
            if (part.Quantity < procedure.PartQuantity || part.AvailableFrom > window.Start) reasons.Add("Required parts are unavailable at the start of the window.");
            result.Add(new($"{window.Id}:{crew.Id}", window.Id, window.Start, end, crew.Id, part.Code, procedure.PartQuantity,
                window.ExpectedOutputMw * procedure.DurationHours, reasons.Count == 0, reasons.ToArray(),
                [scenario.Telemetry.Source.Id, scenario.History.Source.Id, procedure.Source.Id, window.Source.Id, crew.Source.Id, part.Source.Id]));
        }
        return result;
    }
}