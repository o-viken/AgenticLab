using AgenticLab.Extensibility.Runtime;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Demo.Tools;

/// <summary>Publishes the existing Wikipedia and calculator capabilities to explicitly registered examples.</summary>
public sealed class DemoToolSource(WikiTool wiki, CalculatorTool calculator) : IHostToolSource
{
    private readonly IReadOnlyDictionary<string, AITool> _tools = wiki.AsTools()
        .Concat(calculator.AsTools()).ToDictionary(tool => tool.Name, StringComparer.Ordinal);

    /// <inheritdoc />
    public IList<AITool> GetTools(IReadOnlyCollection<string> names) => names.Select(name =>
        _tools.TryGetValue(name, out var tool) ? tool
            : throw new ArgumentException($"Host tool '{name}' is not published.", nameof(names))).ToList();
}