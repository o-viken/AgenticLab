using System.ComponentModel;
using System.Text.Json;
using AgenticLab.Extensibility.Runtime;
using AgenticLab.Examples.Windfarm.Agents;
using AgenticLab.Examples.Windfarm.Data;
using AgenticLab.Examples.Windfarm.Process;
using Microsoft.Extensions.AI;

namespace AgenticLab.Examples.Windfarm.Tools;

internal sealed class WindfarmTools(IWindfarmProcess process, IAgentRunContext context, IAgentDelegation delegation)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    private string Conversation => context.Current is { } identity
        && string.Equals(identity.AgentName, WindfarmCoordinator.AgentName, StringComparison.OrdinalIgnoreCase)
        ? identity.ConversationId : throw new WindfarmException(409, "A host-bound Windfarm coordinator run is required.");

    [Description("Read the active synthetic case, its scenario ID, evidence revision, proposal and review state. Start a case in the example panel if none exists.")]
    public string WindfarmGetCase() => Serialize(process.Find(Conversation)
        ?? throw new WindfarmException(409, "Start a scenario in the Windfarm panel first."));

    [Description("Evaluate maintenance windows deterministically, including blocked alternatives and all required source IDs. Lost MWh = expected MW multiplied by inspection hours.")]
    public string WindfarmEvaluateOptions() => Serialize(process.Options(Conversation));

    [Description("Draft or revise a feasible maintenance option with evidence references. Every revision invalidates previous specialist reviews. Cannot change an awaiting-approval proposal.")]
    public string WindfarmDraftPlan(
        [Description("An exact eligible option ID from WindfarmEvaluateOptions.")] string optionId,
        [Description("Evidence-based rationale and uncertainty; at most 3000 characters.")] string rationale,
        [Description("All source IDs listed by the chosen evaluated option.")] string[] sourceIds) =>
        Serialize(process.Draft(Conversation, optionId, rationale, sourceIds));

    [Description("Freeze the current feasible draft after all three specialists have returned ready reviews for this revision. Does NOT approve or create a work order.")]
    public string WindfarmSubmitProposal() => Serialize(process.Submit(Conversation));

    [Description("Delegate a review of the current draft to windfarm-reliability, windfarm-planning or windfarm-risk over A2A. The host supplies the actual draft and evidence and records a revision-bound receipt.")]
    public async Task<string> DelegateToAgent(
        [Description("Exactly windfarm-reliability, windfarm-planning or windfarm-risk.")] string agentName,
        [Description("A focused review question, at most 3000 characters; not an approval instruction.")] string question,
        CancellationToken cancellationToken = default)
    {
        if (!WindfarmProcess.Reviewers.Contains(agentName, StringComparer.Ordinal)) throw new WindfarmException(400, "Specialist is not allowlisted.");
        if (string.IsNullOrWhiteSpace(question) || question.Length > 3000) throw new WindfarmException(400, "A bounded review question is required.");
        var conversation = Conversation;
        var snapshot = process.Find(conversation) ?? throw new WindfarmException(409, "No active case.");
        if (snapshot.Stage != CaseStage.Draft || snapshot.Draft is null) throw new WindfarmException(409, "Draft the maintenance plan before requesting reviews.");
        var packet = Serialize(new { focusQuestion = question, caseId = snapshot.Id, draft = snapshot.Draft, evidence = WindfarmFixtures.Get(snapshot.Scenario.Id) });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var response = await delegation.InvokeAsync(agentName, packet, deadline.Token);
        deadline.Token.ThrowIfCancellationRequested();
        if (!response.Success) throw new WindfarmException(503, "Specialist unavailable; no review receipt was recorded.");
        if (response.Text.Length > 16_000) throw new WindfarmException(400, "Specialist response exceeded the review limit.");
        ReviewAnswer answer;
        try
        {
            using var document = JsonDocument.Parse(response.Text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("verdict", out var verdict) || verdict.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("observations", out _) || !root.TryGetProperty("concerns", out _) || !root.TryGetProperty("sourceIds", out _))
                throw new JsonException();
            answer = JsonSerializer.Deserialize<ReviewAnswer>(response.Text, Json) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new WindfarmException(400, "Specialist returned a malformed review; no receipt was recorded.");
        }
        var reviewed = process.RecordReview(conversation, snapshot.Id, agentName, snapshot.Revision, answer);
        return Serialize(new { caseId = snapshot.Id, revision = snapshot.Revision,
            suppliedSourceIds = snapshot.Draft.SourceIds, review = reviewed.Reviews.Single(review => review.AgentName == agentName) });
    }

    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(WindfarmGetCase), AIFunctionFactory.Create(WindfarmEvaluateOptions),
        AIFunctionFactory.Create(WindfarmDraftPlan), AIFunctionFactory.Create(WindfarmSubmitProposal),
        AIFunctionFactory.Create(DelegateToAgent),
    ];

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
}