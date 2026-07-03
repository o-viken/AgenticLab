using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace TheSeries.AiService.Application.Tools;

/// <summary>
/// Fetches the contents of a public web page or HTTP API on the agent's behalf. Unlike the file and
/// terminal tools this reaches <em>out</em> to the network rather than the workspace, so it is the
/// backend counterpart of a VS Code custom agent's <c>web/fetch</c> tool. For safety it only follows
/// absolute <c>http</c>/<c>https</c> URLs, caps the returned text, and relies on the injected
/// <see cref="HttpClient"/>'s timeout to bound a slow response.
/// </summary>
public sealed class WebFetchTool(HttpClient http)
{
    private const int MaxChars = 20_000;

    /// <summary>Fetches an absolute http(s) URL and returns its response body as text.</summary>
    /// <param name="url">The absolute <c>http://</c> or <c>https://</c> URL to fetch.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The response body (truncated when large), or a message describing why the fetch failed.</returns>
    [Description("Fetch the contents of a public http(s) web page or HTTP API and return its text.")]
    public async Task<string> WebFetch(
        [Description("The absolute http:// or https:// URL to fetch, e.g. 'https://example.com'.")] string url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Provide an absolute http:// or https:// URL.";
        }

        try
        {
            using var response = await http.GetAsync(uri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return $"Request to '{uri}' failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
            }

            return body.Length > MaxChars
                ? body[..MaxChars] + $"\n… (truncated, {body.Length} characters total)"
                : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"Could not fetch '{uri}': {ex.Message}";
        }
    }

    /// <summary>Exposes this tool's method as an AI tool for an agent.</summary>
    public IList<AITool> AsTools() => [AIFunctionFactory.Create(WebFetch)];
}
