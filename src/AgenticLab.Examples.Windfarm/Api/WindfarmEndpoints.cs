using AgenticLab.Examples.Windfarm.Data;
using AgenticLab.Examples.Windfarm.Process;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AgenticLab.Examples.Windfarm.Api;

internal static class WindfarmEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.WithGroupName(WindfarmExample.Id);
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
            try { return await next(context); }
            catch (WindfarmException error)
            {
                return TypedResults.Problem(statusCode: error.StatusCode, title: "Windfarm sandbox request rejected", detail: error.Message);
            }
        });

        group.MapGet("/scenarios", (CancellationToken cancellationToken) => TypedResults.Ok(WindfarmFixtures.List()))
            .WithSummary("List deterministic synthetic wind-farm scenarios.");
        group.MapGet("/scenarios/{scenarioId}/telemetry", (string scenarioId, CancellationToken cancellationToken) => TypedResults.Ok(WindfarmFixtures.Get(scenarioId).Telemetry))
            .WithSummary("Read synthetic turbine telemetry with units, timestamp and source ID.");
        group.MapGet("/scenarios/{scenarioId}/history", (string scenarioId, CancellationToken cancellationToken) => TypedResults.Ok(WindfarmFixtures.Get(scenarioId).History))
            .WithSummary("Read synthetic maintenance history; findings are not a diagnosis.");
        group.MapGet("/scenarios/{scenarioId}/weather", (string scenarioId, CancellationToken cancellationToken) => TypedResults.Ok(WindfarmFixtures.Get(scenarioId).Windows))
            .WithSummary("Read repeatable forecast windows relative to scenario time.");
        group.MapGet("/scenarios/{scenarioId}/resources", (string scenarioId, CancellationToken cancellationToken) =>
        {
            var scenario = WindfarmFixtures.Get(scenarioId);
            return TypedResults.Ok(new ResourceResponse(scenario.Crews, scenario.Parts));
        }).WithSummary("Read synthetic crew qualifications and parts availability.");
        group.MapGet("/scenarios/{scenarioId}/procedure", (string scenarioId, CancellationToken cancellationToken) => TypedResults.Ok(WindfarmFixtures.Get(scenarioId).Procedure))
            .WithSummary("Read illustrative inspection constraints, not a certified operating procedure.");

        group.MapPost("/cases", (CreateCaseRequest request, IWindfarmProcess process, CancellationToken cancellationToken) =>
        {
            var created = process.Create(request.ConversationId, request.ScenarioId);
            return TypedResults.Created($"{WindfarmExample.ApiPath}/cases/{created.Id}?conversationId={Uri.EscapeDataString(created.ConversationId)}", created);
        }).WithSummary("Start an isolated synthetic case bound to a conversation.");
        group.MapGet("/cases/active", IResult (string conversationId, IWindfarmProcess process, CancellationToken cancellationToken) =>
            process.Find(conversationId) is { } current ? TypedResults.Ok(current) : TypedResults.NoContent())
            .Produces<CaseSnapshot>().Produces(StatusCodes.Status204NoContent)
            .WithSummary("Find the active case without silently creating one.");
        group.MapGet("/cases/{caseId}", (string caseId, string conversationId, IWindfarmProcess process, CancellationToken cancellationToken) =>
            TypedResults.Ok(process.Get(conversationId, caseId))).WithSummary("Read the authoritative case and bounded audit.");
        group.MapGet("/cases/{caseId}/options", (string caseId, string conversationId, IWindfarmProcess process, CancellationToken cancellationToken) =>
        {
            process.Get(conversationId, caseId);
            return TypedResults.Ok(process.Options(conversationId, caseId));
        }).WithSummary("Evaluate windows against deterministic evidence, weather, crew and inventory rules.");
        group.MapPost("/cases/{caseId}/drafts", (string caseId, DraftRequest request, IWindfarmProcess process, CancellationToken cancellationToken) =>
        {
            process.Get(request.ConversationId, caseId);
            return TypedResults.Ok(process.Draft(request.ConversationId, request.OptionId, request.Rationale, request.SourceIds, caseId));
        }).WithSummary("Revise a plan; invalidates reviews and cannot modify a submitted proposal.");
        group.MapPost("/cases/{caseId}/proposals", (string caseId, CaseCommandRequest request, IWindfarmProcess process, CancellationToken cancellationToken) =>
        {
            process.Get(request.ConversationId, caseId);
            return TypedResults.Ok(process.Submit(request.ConversationId, caseId));
        }).WithSummary("Freeze a feasible plan after all current specialist reviews succeed.");
        group.MapPost("/cases/{caseId}/decisions", (string caseId, DecisionRequest request, IWindfarmProcess process, CancellationToken cancellationToken) =>
            TypedResults.Ok(process.Decide(request.ConversationId, caseId, request.Revision, request.ProposalHash,
                request.Decision, request.IdempotencyKey, request.Reason)))
            .WithSummary("Record an explicit human decision; approval atomically creates one simulated planned order.")
            .WithDescription("Trusted local demo command, never advertised as an agent tool. Revision/hash binding and idempotency are enforced; production user authentication is not provided.");
        group.MapPost("/cases/{caseId}/archive", (string caseId, CaseCommandRequest request, IWindfarmProcess process, CancellationToken cancellationToken) =>
            TypedResults.Ok(process.Archive(request.ConversationId, caseId)))
            .WithSummary("Archive a case and invalidate outstanding decisions without affecting other conversations.");

        if (((IEndpointRouteBuilder)group).ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            group.MapOpenApi("/openapi/{documentName}.json");
    }
}

/// <summary>Starts a new isolated fixture instance for an explicit conversation.</summary>
internal sealed record CreateCaseRequest(string ConversationId, string ScenarioId);
/// <summary>Identifies the conversation owning the route's case.</summary>
internal sealed record CaseCommandRequest(string ConversationId);
/// <summary>Proposes a known maintenance option with evidence references and bounded rationale.</summary>
internal sealed record DraftRequest(string ConversationId, string OptionId, string Rationale, string[] SourceIds);
/// <summary>A human command bound to the exact displayed proposal, separate from all model tools.</summary>
internal sealed record DecisionRequest(string ConversationId, int Revision, string ProposalHash, string Decision, string IdempotencyKey, string? Reason = null);
/// <summary>Read-only synthetic resources; reservations are isolated within each case.</summary>
internal sealed record ResourceResponse(IReadOnlyList<Crew> Crews, IReadOnlyList<Part> Parts);