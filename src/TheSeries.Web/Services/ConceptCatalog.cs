using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Markdig;

namespace TheSeries.Web;

/// <summary>
/// Loads the in-app learning content shown by the concept drawer. Each concept lives in its own folder
/// under <c>Concepts/</c> next to the app, combining structured metadata with a markdown body:
/// <code>
/// Concepts/tools/meta.json   { "title", "summary", "category", "links": [{ "label", "url" }] }
/// Concepts/tools/body.md     (markdown rendered to HTML at load time)
/// </code>
/// The catalogue is read once at construction (the content is copied next to the app and changes only
/// on a rebuild/restart), the markdown bodies are rendered with <see cref="Markdig"/>, and the result is
/// cached in an immutable, case-insensitive dictionary keyed by the folder name. Registered as a singleton.
/// </summary>
public sealed class ConceptCatalog
{
    private const string ConceptsFolder = "Concepts";
    private const string MetaFileName = "meta.json";
    private const string BodyFileName = "body.md";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private readonly ImmutableDictionary<string, Concept> _concepts;
    private readonly ImmutableArray<Concept> _all;

    /// <summary>
    /// Discovers and renders every concept under the app's <c>Concepts/</c> folder. Folders without a
    /// readable <c>meta.json</c> are skipped; a missing <c>body.md</c> yields an empty body.
    /// </summary>
    /// <param name="environment">Provides the content root used to locate the <c>Concepts/</c> folder.</param>
    /// <param name="logger">Logs folders that could not be parsed.</param>
    public ConceptCatalog(IWebHostEnvironment environment, ILogger<ConceptCatalog> logger)
    {
        var root = Path.Combine(environment.ContentRootPath, ConceptsFolder);
        var builder = ImmutableDictionary.CreateBuilder<string, Concept>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(root))
        {
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                var id = Path.GetFileName(folder);
                var metaPath = Path.Combine(folder, MetaFileName);
                if (!File.Exists(metaPath))
                {
                    continue;
                }

                try
                {
                    var meta = JsonSerializer.Deserialize<ConceptMeta>(File.ReadAllText(metaPath), JsonOptions);
                    if (meta is null || string.IsNullOrWhiteSpace(meta.Title))
                    {
                        continue;
                    }

                    var bodyPath = Path.Combine(folder, BodyFileName);
                    var bodyHtml = File.Exists(bodyPath)
                        ? Markdown.ToHtml(File.ReadAllText(bodyPath), MarkdownPipeline)
                        : string.Empty;

                    builder[id] = new Concept(
                        id,
                        meta.Title,
                        meta.Summary ?? string.Empty,
                        meta.Category ?? "core",
                        meta.Links ?? [],
                        bodyHtml);
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    logger.LogWarning(ex, "Failed to load concept from {Folder}.", folder);
                }
            }
        }
        else
        {
            logger.LogWarning("Concepts folder not found at {Root}.", root);
        }

        _concepts = builder.ToImmutable();
        _all = _concepts.Values
            .OrderBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    /// <summary>Every loaded concept, ordered by category then title (used by the Learn panel's index).</summary>
    public IReadOnlyList<Concept> All => _all;

    /// <summary>Returns the concept with the given id (case-insensitive), or <c>null</c> if none exists.</summary>
    public Concept? Get(string id) =>
        !string.IsNullOrWhiteSpace(id) && _concepts.TryGetValue(id, out var concept) ? concept : null;

    /// <summary>True when a concept with the given id exists.</summary>
    public bool Has(string id) => !string.IsNullOrWhiteSpace(id) && _concepts.ContainsKey(id);
}

/// <summary>A single learning topic: its id, title, summary, category, external links and rendered body.</summary>
public sealed record Concept(
    string Id,
    string Title,
    string Summary,
    string Category,
    IReadOnlyList<ConceptLink> Links,
    string BodyHtml);

/// <summary>A labelled external link shown in the concept drawer.</summary>
public sealed record ConceptLink(string Label, string Url);

/// <summary>The shape of a concept's <c>meta.json</c> file.</summary>
internal sealed record ConceptMeta(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("links")] IReadOnlyList<ConceptLink>? Links);
