using System.ComponentModel;
using Microsoft.Extensions.AI;
using TheSeries.AiService.Application;

namespace TheSeries.AiService.Application.Tools;

/// <summary>
/// A capability that lets an agent pause mid-run to ask the user a clarifying question and continue
/// once the answer arrives. It is intended for genuinely blocking ambiguities — the agent should ask
/// sparingly and otherwise proceed with stated assumptions.
/// </summary>
/// <remarks>
/// The tool blocks on the run's <see cref="UserInputScope"/> (only opened by the streaming
/// <c>/chat/stream</c> path); the <c>POST /chat/control</c> endpoint releases it with the user's
/// answer, which is returned as the tool result. Under the non-interactive <c>/chat</c> endpoint no
/// scope is active, so it returns a fallback telling the model to proceed without asking.
/// </remarks>
public sealed class AskQuestionTool
{
    /// <summary>Asks the user a question and waits for their answer.</summary>
    /// <param name="question">The question to put to the user.</param>
    /// <param name="cancellationToken">A token that abandons the wait if the run is stopped.</param>
    /// <returns>The user's answer, or a fallback when the run is not interactive.</returns>
    [Description("Ask the user a clarifying question and wait for their answer. Use only for a genuinely blocking ambiguity you cannot resolve from the workspace or context; otherwise proceed and state your assumptions.")]
    public async Task<string> AskQuestion(
        [Description("The question to put to the user, phrased clearly and answerable in a sentence.")] string question,
        CancellationToken cancellationToken = default)
    {
        var scope = UserInputScope.Current;
        if (scope is null)
        {
            return "Cannot ask the user a question in this non-interactive mode. Proceed with reasonable assumptions and state them clearly.";
        }

        return await scope.WaitForAnswerAsync(cancellationToken);
    }

    /// <summary>Exposes this tool's method as an <see cref="AITool"/> for an agent.</summary>
    /// <returns>The tool list to merge into an agent's tools.</returns>
    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(AskQuestion),
    ];
}
