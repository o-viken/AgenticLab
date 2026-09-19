using System.Text.Json;

namespace TheSeries.Web.Flow;

/// <summary>A read-only detail block; its source distinguishes current choices from historical captures.</summary>
internal sealed record HostDetailBlock(string Title, string Text, string Source);

/// <summary>Builds only the selected layer, without retaining payloads or mutating execution.</summary>
internal static class HostDetailsBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Finds a request in the displayed exchange, never beyond the replay cursor or under another configuration.</summary>
    public static FlowEvent? RequestFor(FlowViewState view, ExecutionExchange? exchange, int? sequence, bool replaying)
    {
        if (exchange is null || exchange.Agent != view.SelectedAgent || exchange.Vendor != view.VendorKey
            || (exchange.Workspace ?? "") != view.Workspace) return null;
        var stages = replaying ? ExecutionReplayBuilder.PrefixThrough(exchange, sequence) : exchange.Stages;
        return stages.LastOrDefault(stage => stage.Kind == "llm-request" && !string.IsNullOrWhiteSpace(stage.Data));
    }

    /// <summary>Reads a top-level captured field as complete, encoded plain text; malformed captures stay unavailable.</summary>
    public static string? CapturedField(string? json, string field)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(PromptSignatureBuilder.ToStrictJson(json));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(field, out var value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString()
                : JsonSerializer.Serialize(value, JsonOptions);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Projects current configuration and explicitly attributed captures for one host section.</summary>
    public static IReadOnlyList<HostDetailBlock> Build(HostDetailSection section, FlowViewState view, FlowRunController run)
    {
        var blocks = new List<HostDetailBlock>();
        var exchange = run.Replay.SelectedExchange;
        var request = RequestFor(view, exchange, run.Replay.SelectedStage?.Sequence, run.Replay.Replaying);
        var provenance = exchange is null ? "Not captured" :
            $"Captured · exchange {exchange.Number} · {exchange.Agent} · {exchange.Vendor ?? "Default"}"
            + (request is null ? "" : $" · turn {request.Turn}")
            + (string.IsNullOrEmpty(exchange.Workspace) ? "" : $" · {exchange.Workspace}");
        const string current = "Current configuration";
        void Add(string title, string? text, string source = current) =>
            blocks.Add(new(title, string.IsNullOrWhiteSpace(text) ? "Not available." : text, source));

        switch (section)
        {
            case HostDetailSection.Client:
                Add("Client", "Blazor web app: sends the user message and renders streamed replies.", "Set by application");
                break;
            case HostDetailSection.SystemPrompt:
                Add("System prompt", view.Harness.HasPromptText ? view.Harness.PromptText : view.Harness.PromptAvailability, "Set by application · " + view.Harness.Label);
                break;
            case HostDetailSection.Persona:
                var persona = PromptSignatureBuilder.AgentModeText(CapturedField(request?.Data, "instructions"));
                Add("Agent persona", persona.Length == 0 ? view.Agent.Description : persona,
                    persona.Length == 0 ? "Agent description; persona text not captured." : provenance);
                break;
            case HostDetailSection.Tools:
                var definitions = CapturedField(request?.Data, "tools");
                if (definitions is not null)
                {
                    Add("Tools", definitions, provenance);
                    break;
                }
                foreach (var tool in view.Agent.ToolNames)
                    Add(tool, view.Options.IsToolEnabled(tool) ? "Enabled" : "Disabled");
                if (view.Agent.ToolNames.Count == 0) Add("Tools", "No tools configured.");
                break;
            case HostDetailSection.Settings:
                Add("Settings", $"Provider: Azure OpenAI\nDeployment: {(string.IsNullOrWhiteSpace(view.Agent.Info?.ModelId) ? "Service default" : view.Agent.Info.ModelId)}", "Set per agent");
                break;
            case HostDetailSection.Instructions:
                foreach (var instruction in run.Catalogs.KnownInstructions)
                    Add(instruction.Name, $"{(view.Options.IsInstructionEnabled(instruction.Name) ? "Enabled" : "Disabled")}\n{instruction.Description}", "Workspace catalogue · current configuration");
                if (blocks.Count == 0) Add("Custom instructions", view.Agent.SupportsInstructions ? "No instructions available for this workspace." : "Not applicable to this agent.");
                break;
            case HostDetailSection.Skills:
                foreach (var skill in run.Catalogs.KnownSkills)
                    Add(skill.Name, $"{(view.Options.IsSkillEnabled(skill.Name) ? "Enabled" : "Disabled")}\n{skill.Description}", "Workspace catalogue · current configuration");
                if (blocks.Count == 0) Add("Skills", view.Agent.SupportsSkills ? "No skills available for this workspace." : "Not applicable to this agent.");
                break;
            case HostDetailSection.Mcp:
                foreach (var tool in run.Catalogs.KnownMcp) Add(tool.Name, tool.Description, "Discovered MCP tool");
                if (blocks.Count == 0) Add("MCP servers", view.Agent.SupportsMcp ? "No connected tools available." : "Not applicable to this agent.");
                break;
            case HostDetailSection.A2A:
                foreach (var agent in run.Catalogs.KnownA2A) Add(agent.Name, agent.Description, "Discovered A2A agent");
                if (blocks.Count == 0) Add("A2A agents", view.Agent.SupportsA2A ? "No connected agents available." : "Not applicable to this agent.");
                break;
            case HostDetailSection.UserPrompt:
                Add("User prompt", exchange?.Message ?? "No message submitted.", provenance);
                break;
            case HostDetailSection.Context:
                if (exchange is not null)
                {
                    var prefix = run.Replay.Replaying ? ExecutionReplayBuilder.PrefixThrough(exchange, run.Replay.SelectedStage?.Sequence) : exchange.Stages;
                    var contextRequest = prefix.LastOrDefault(stage => stage.Kind == "llm-request" && stage.Data is not null);
                    Add("Messages sent to the model", CapturedField(contextRequest?.Data, "messages") ?? "Not captured at this position.", provenance);
                    foreach (var stage in prefix.Where(stage => FlowEventMapping.IsContentEvent(stage) && stage.Kind != "received"))
                        Add(ExecutionReplayBuilder.StageTitle(stage), stage.Data ?? stage.Detail, provenance);
                }
                    else Add("Context", "No content captured.");
                break;
            case HostDetailSection.Environment:
                Add("Where it runs", view.Agent.EnvTitle);
                Add("Risk", view.Agent.RiskLevel);
                Add("Guardrails", view.Agent.Guardrails.Count == 0 ? "No additional guardrails declared." : string.Join("\n", view.Agent.Guardrails));
                break;
        }
        return blocks;
    }
}