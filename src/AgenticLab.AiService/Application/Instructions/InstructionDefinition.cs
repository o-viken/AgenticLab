namespace AgenticLab.AiService.Application.Instructions;

/// <summary>
/// A custom instruction file discovered in the active workspace: project-specific guidance that is
/// <em>always applied</em> when present, modelled on GitHub Copilot's custom instructions. Unlike a
/// skill (which is loaded on demand via a tool), an instruction's full <see cref="Body"/> is injected
/// into the agent's context on every run, so the model always follows it.
/// </summary>
/// <param name="Name">The instruction's name, taken from the <c>name</c> frontmatter field or the file name.</param>
/// <param name="Description">A short summary from the optional <c>description</c> frontmatter field; may be empty.</param>
/// <param name="Body">The instruction text (the markdown body after any frontmatter) injected into the model's context.</param>
/// <param name="RelativePath">The workspace-relative path to the file, e.g. <c>instructions/code-style.instructions.md</c>.</param>
internal sealed record InstructionDefinition(string Name, string Description, string Body, string RelativePath);
