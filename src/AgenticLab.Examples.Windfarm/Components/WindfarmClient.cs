using System.Net;
using System.Net.Http.Json;
using AgenticLab.Examples.Windfarm.Api;
using AgenticLab.Examples.Windfarm.Process;

namespace AgenticLab.Examples.Windfarm.Components;

internal interface IWindfarmClient
{
    Task<IReadOnlyList<ScenarioSummary>> ScenariosAsync(CancellationToken cancellationToken);
    Task<CaseSnapshot?> ActiveAsync(string conversationId, CancellationToken cancellationToken);
    Task<CaseSnapshot> StartAsync(string conversationId, string scenarioId, CancellationToken cancellationToken);
    Task<CaseSnapshot> DecideAsync(string caseId, DecisionRequest request, CancellationToken cancellationToken);
    Task ArchiveAsync(string conversationId, string caseId, CancellationToken cancellationToken);
}

internal sealed class WindfarmClient(HttpClient http) : IWindfarmClient
{
    public async Task<IReadOnlyList<ScenarioSummary>> ScenariosAsync(CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<ScenarioSummary[]>($"{WindfarmExample.ApiPath}/scenarios", cancellationToken) ?? [];

    public async Task<CaseSnapshot?> ActiveAsync(string conversationId, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"{WindfarmExample.ApiPath}/cases/active?conversationId={Uri.EscapeDataString(conversationId)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        return await Read(response, cancellationToken);
    }

    public async Task<CaseSnapshot> StartAsync(string conversationId, string scenarioId, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"{WindfarmExample.ApiPath}/cases", new CreateCaseRequest(conversationId, scenarioId), cancellationToken);
        return await Read(response, cancellationToken);
    }

    public async Task<CaseSnapshot> DecideAsync(string caseId, DecisionRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"{WindfarmExample.ApiPath}/cases/{Uri.EscapeDataString(caseId)}/decisions", request, cancellationToken);
        return await Read(response, cancellationToken);
    }

    public async Task ArchiveAsync(string conversationId, string caseId, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync($"{WindfarmExample.ApiPath}/cases/{Uri.EscapeDataString(caseId)}/archive", new CaseCommandRequest(conversationId), cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound) await Read(response, cancellationToken);
    }

    private static async Task<CaseSnapshot> Read(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(cancellationToken);
            throw new HttpRequestException(problem?.Detail ?? "Windfarm service unavailable.", null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<CaseSnapshot>(cancellationToken)
            ?? throw new HttpRequestException("Windfarm service returned an empty case.");
    }

    private sealed record ApiProblem(string? Detail);
}