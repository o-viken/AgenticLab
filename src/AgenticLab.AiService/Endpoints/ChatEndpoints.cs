using Microsoft.Agents.AI;

namespace AgenticLab.AiService.Endpoints;

/// <summary>The chat endpoints: a single non-interactive turn, the paced streaming run, its control channel and conversation reset.</summary>
internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/chat", ChatAsync);

        // Streams the steps of an agent run as Server-Sent Events so the web UI can animate the data flow live.
        // The run is paced by a FlowSession so the client can step, pause, resume or stop the real execution.
        app.MapPost("/chat/stream", IResult (FlowChatRequest request, FlowTracer tracer, FlowControlRegistry registry, CancellationToken cancellationToken) =>
        {
            if (request.Breakpoints?.Any(kind => !FlowSession.BreakpointKinds.Contains(kind)) == true)
            {
                return Results.BadRequest("Unknown breakpoint kind.");
            }

            var session = registry.Create(request.SessionId, request.Manual, request.StepDelayMs);
            session.SetBreakpoints(request.Breakpoints ?? []);
            return TypedResults.ServerSentEvents(
                tracer.StreamAsync(request.Message, request.Agent, request.ConversationId, request.Workspace, request.DisabledTools, request.DisabledSkills, request.EnabledInstructions, request.Vendor, session, cancellationToken),
                eventType: "flow");
        });

        app.MapPost("/chat/control", Control);

        // Clears a conversation's remembered history so the next message starts fresh.
        app.MapPost("/chat/reset", (ConversationResetRequest request, ConversationStore conversations) =>
        {
            if (string.IsNullOrWhiteSpace(request.ConversationId))
            {
                return Results.BadRequest("ConversationId must not be empty.");
            }

            conversations.Reset(request.ConversationId.Trim());
            return Results.NoContent();
        });

        return app;
    }

    // One non-interactive turn against a built-in or workspace-defined agent.
    private static async Task<IResult> ChatAsync(ChatRequest request, AgentCatalog catalog, WorkspaceAgentResolver workspaceAgents, ConversationStore conversations, SkillLoader skills, InstructionLoader instructions, VendorHarnessCatalog vendors, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return Results.BadRequest("Message must not be empty.");
        }

        // When a brand/vendor is selected, its harness replaces the shared harness for this run.
        var harness = vendors.Resolve(request.Vendor);

        // A built-in agent: resolve it and open a workspace only when it requires one.
        if (catalog.TryResolve(request.Agent, harness, out var agent, out var resolvedName))
        {
            if (catalog.RequiresWorkspace(resolvedName) && string.IsNullOrWhiteSpace(request.Workspace))
            {
                return Results.BadRequest($"Agent '{resolvedName}' requires a workspace. Include a 'workspace' path in the request.");
            }

            using var workspace = catalog.RequiresWorkspace(resolvedName) ? WorkspaceScope.TryBegin(request.Workspace) : null;
            if (catalog.RequiresWorkspace(resolvedName) && workspace is null)
            {
                return Results.BadRequest($"Workspace path '{request.Workspace}' is not an existing directory.");
            }

            return await RunChatAsync(agent, resolvedName, catalog.SupportsSkills(resolvedName), request, conversations, skills, instructions, cancellationToken);
        }

        // Otherwise it may be a user-authored agent declared in the workspace's agents/ folder, which can
        // only be discovered once a (valid) workspace is open.
        if (string.IsNullOrWhiteSpace(request.Workspace))
        {
            return Results.BadRequest($"Unknown agent '{request.Agent}'. Call GET /agents for the available names.");
        }

        using var agentWorkspace = WorkspaceScope.TryBegin(request.Workspace);
        if (agentWorkspace is null)
        {
            return Results.BadRequest($"Workspace path '{request.Workspace}' is not an existing directory.");
        }

        if (!workspaceAgents.TryResolve(request.Agent, out var workspaceAgent, out var definition, harness))
        {
            return Results.BadRequest($"Unknown agent '{request.Agent}'. Call GET /agents for the available names.");
        }

        // Workspace-defined agents always support workspace skills (see WorkspaceDefinedAgent).
        return await RunChatAsync(workspaceAgent, definition.Name, supportsSkills: true, request, conversations, skills, instructions, cancellationToken);
    }

    // Runs one turn, continuing the supplied conversation, injecting the enabled custom instructions and
    // (when the agent uses skills) the skill catalogue, and hiding any tools the caller disabled. Any
    // required workspace scope must already be active on the calling context.
    private static async Task<IResult> RunChatAsync(AIAgent agent, string resolvedName, bool supportsSkills, ChatRequest request, ConversationStore conversations, SkillLoader skills, InstructionLoader instructions, CancellationToken cancellationToken)
    {
        // Continue the existing conversation when an id is supplied; otherwise mint a new one and return
        // it so the client can keep the conversation going.
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("n")
            : request.ConversationId.Trim();
        var session = await conversations.GetOrCreateAsync(conversationId, agent, cancellationToken);

        // Non-interactive: an AskQuestion tool falls back to stated assumptions instead of blocking.
        using var scopes = RunScopeSet.Begin(null, request.DisabledTools, request.DisabledSkills, request.EnabledInstructions, interactive: false);
        var runOptions = RunScopeSet.BuildRunOptions(supportsSkills, skills, instructions);

        var response = await agent.RunAsync(request.Message, session, runOptions, cancellationToken);
        return Results.Ok(new ChatResponse(response.Text, resolvedName, conversationId));
    }

    // Drives an in-flight /chat/stream run: single-step (next), pause, resume, switch mode, change the
    // delay, answer a tool's question, release a breakpoint or stop. Matches the run by its session id.
    private static IResult Control(FlowControlRequest request, FlowControlRegistry registry)
    {
        if (!registry.TryGet(request.SessionId, out var session))
        {
            return Results.NotFound();
        }

        if (request.Breakpoints is { } breakpoints)
        {
            if (breakpoints.Any(kind => !FlowSession.BreakpointKinds.Contains(kind)))
            {
                return Results.BadRequest("Unknown breakpoint kind.");
            }
            session.SetBreakpoints(breakpoints);
        }

        if (request.BreakpointId is { } breakpointId)
        {
            var action = request.Action?.ToLowerInvariant();
            if (action is not ("next" or "resume"))
            {
                return Results.BadRequest("A breakpoint requires next or resume.");
            }
            return session.ReleaseBreakpoint(breakpointId, manual: action == "next")
                ? Results.NoContent()
                : Results.Conflict("This breakpoint is no longer paused.");
        }

        if (request.Manual is { } manual)
        {
            session.Manual = manual;
        }

        if (request.DelayMs is { } delay)
        {
            session.DelayMs = delay;
        }

        switch (request.Action?.ToLowerInvariant())
        {
            case "next":
                session.Advance();
                break;
            case "pause":
                session.Paused = true;
                break;
            case "resume":
                session.Paused = false;
                break;
            case "stop":
                session.Stop();
                break;
            case "answer":
                session.UserInput?.ProvideAnswer(request.Answer ?? string.Empty);
                break;
        }

        return Results.NoContent();
    }
}

internal sealed record ChatRequest(string Message, string? Agent = null, string? ConversationId = null, string? Workspace = null, IReadOnlyList<string>? DisabledTools = null, IReadOnlyList<string>? DisabledSkills = null, IReadOnlyList<string>? EnabledInstructions = null, string? Vendor = null);
internal sealed record ChatResponse(string Reply, string Agent, string ConversationId);
internal sealed record FlowChatRequest(string Message, string? Agent, string SessionId, string ConversationId, bool Manual = false, int StepDelayMs = 0, string? Workspace = null, IReadOnlyList<string>? DisabledTools = null, IReadOnlyList<string>? DisabledSkills = null, IReadOnlyList<string>? EnabledInstructions = null, string? Vendor = null, IReadOnlyList<string>? Breakpoints = null);
internal sealed record FlowControlRequest(string SessionId, string? Action = null, bool? Manual = null, int? DelayMs = null, string? Answer = null, IReadOnlyList<string>? Breakpoints = null, string? BreakpointId = null);
internal sealed record ConversationResetRequest(string ConversationId);
