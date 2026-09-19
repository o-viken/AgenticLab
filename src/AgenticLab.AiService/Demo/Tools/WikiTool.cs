using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Demo.Tools;

/// <summary>
/// A single capability exposed to the agent: searching Wikipedia and looking up a page.
/// Backed by the public Wikipedia REST API.
/// </summary>
public sealed class WikiTool(HttpClient http)
{

    /// <summary>Searches Wikipedia and returns matching page titles with short descriptions.</summary>
    /// <param name="query">The words to search for.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A newline-separated list of matching page titles, or a message when nothing matches.</returns>
    [Description("Search Wikipedia for articles matching a query. Returns a list of matching page titles with short descriptions.")]
    public async Task<string> SearchWiki(
        [Description("The words to search for, e.g. 'Alan Turing'.")] string query,
        CancellationToken cancellationToken = default)
    {
        var url = $"/w/rest.php/v1/search/page?q={Uri.EscapeDataString(query)}&limit=5";
        using var doc = await GetJsonAsync(url, cancellationToken);
        if (doc is null)
        {
            return "No results (the search request failed).";
        }

        var sb = new StringBuilder();
        foreach (var page in doc.RootElement.GetProperty("pages").EnumerateArray())
        {
            var title = page.GetProperty("title").GetString();
            var description = page.TryGetProperty("description", out var d) ? d.GetString() : null;
            sb.AppendLine($"- {title}{(string.IsNullOrWhiteSpace(description) ? "" : $": {description}")}");
        }

        return sb.Length == 0 ? "No matching Wikipedia pages found." : sb.ToString();
    }

    /// <summary>Looks up a single Wikipedia page by its exact title and returns a short summary extract.</summary>
    /// <param name="title">The exact page title to look up.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The page summary extract, or a message when the page is missing or has no summary.</returns>
    [Description("Look up a single Wikipedia page by its exact title and return a short summary extract.")]
    public async Task<string> GetWikiPage(
        [Description("The exact page title, e.g. 'Alan Turing'.")] string title,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/rest_v1/page/summary/{Uri.EscapeDataString(title)}";
        using var doc = await GetJsonAsync(url, cancellationToken);
        if (doc is null)
        {
            return $"Could not find a Wikipedia page titled '{title}'.";
        }

        var extract = doc.RootElement.TryGetProperty("extract", out var e) ? e.GetString() : null;
        return string.IsNullOrWhiteSpace(extract)
            ? $"The page '{title}' exists but has no summary."
            : extract!;
    }

    /// <summary>Exposes this tool's methods as AI tools for an agent.</summary>
    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(SearchWiki),
        AIFunctionFactory.Create(GetWikiPage),
    ];

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}

