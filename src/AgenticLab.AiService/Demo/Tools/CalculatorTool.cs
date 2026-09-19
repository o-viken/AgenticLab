using System.ComponentModel;
using System.Data;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Demo.Tools;

/// <summary>
/// A single capability exposed to the agent: evaluating arithmetic expressions.
/// Backed by <see cref="DataTable.Compute"/>, which supports +, -, *, /, %, and parentheses.
/// </summary>
public sealed class CalculatorTool
{
    /// <summary>Evaluates an arithmetic expression and returns the numeric result.</summary>
    /// <param name="expression">The arithmetic expression to evaluate, e.g. <c>(12 * 9) + 3</c>.</param>
    /// <returns>The computed result, or a message describing why the expression could not be evaluated.</returns>
    [Description("Evaluate an arithmetic expression (supports + - * / % and parentheses) and return the numeric result.")]
    public string Calculate(
        [Description("The arithmetic expression to evaluate, e.g. '(12 * 9) + 3'.")] string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return "The expression was empty.";
        }

        try
        {
            var result = new DataTable().Compute(expression, null);
            return Convert.ToString(result, CultureInfo.InvariantCulture) ?? "(no result)";
        }
        catch (Exception ex) when (ex is EvaluateException or SyntaxErrorException or InvalidCastException or OverflowException)
        {
            return $"Could not evaluate '{expression}': {ex.Message}";
        }
    }

    /// <summary>Exposes this tool's methods as AI tools for an agent.</summary>
    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(Calculate),
    ];
}
