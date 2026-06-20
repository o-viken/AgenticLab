using Microsoft.Extensions.AI;
using TheSeries.AiService.Tools;

namespace TheSeries.AiService.Agents;

/// <summary>
/// The Microsoft 365 Copilot "Analyst" agent: a data analyst that pulls figures from the user's
/// documents (the fake Microsoft 365 / Graph file tools) and crunches the numbers with the calculator.
/// </summary>
public sealed class M365AnalystAgent(Microsoft365Tool m365, CalculatorTool calculator) : AgentDefinitionBase
{
    /// <inheritdoc />
    public override string Name => "M365Analyst";

    /// <inheritdoc />
    public override string Description => "Data analyst that reads figures from your documents and computes the numbers.";

    /// <inheritdoc />
    /// <remarks>Low risk: read-only document reads plus calculation, with no side effects.</remarks>
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails =>
    [
        "Read-only document reads and calculation — never writes anything",
        "Sample, in-memory work data — no real Microsoft Graph or network calls",
        "No file-system or command access on your machine",
        "Individual tools can be toggled off per run",
    ];

    /// <inheritdoc />
    protected override string Persona =>
        "You are Analyst, a Microsoft 365 Copilot agent for quantitative analysis. " +
        "When asked to analyze numbers, first use SearchFiles to find the relevant workbook or document and " +
        "SummarizeDocument to read its figures, then use the Calculate tool to do the arithmetic rather than " +
        "computing in your head. Show the figures you started from and the calculation you ran, and present " +
        "the result clearly. Never fabricate numbers that aren't in the documents.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. m365.AsAnalystTools(), .. calculator.AsTools()];
}
