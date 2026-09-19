using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AgenticLab.AiService.Application.Flow;
using AgenticLab.AiService.Application.Instructions;
using AgenticLab.AiService.Application.Skills;
using AgenticLab.AiService.Application.Workspace;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class RunScopeSetTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("agentic-lab-scopes-").FullName;

    public RunScopeSetTests()
    {
        Write("skills/get-date/SKILL.md", "---\nname: get-date\ndescription: Tells the date.\n---\nRun `date`.\n");
        Write("skills/lint/SKILL.md", "---\nname: lint\ndescription: Lints the repo.\n---\nRun the linter.\n");
        Write("instructions/style.instructions.md", "---\ndescription: House style.\n---\nUse tabs.\n");
        Write("instructions/tests.instructions.md", "---\ndescription: Test rules.\n---\nAlways add a test.\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string? InstructionsOf(AgentRunOptions? options) =>
        Assert.IsType<ChatClientAgentRunOptions>(options).ChatOptions?.Instructions;

    [Fact]
    public void Begin_OpensOnlyTheScopesTheRunNeeds()
    {
        using var scopes = RunScopeSet.Begin(null, disabledTools: null, disabledSkills: null, enabledInstructions: null, interactive: false);

        Assert.Null(ToolFilterScope.Current);
        Assert.Null(SkillFilterScope.Current);
        Assert.NotNull(InstructionFilterScope.Current);
        Assert.Null(UserInputScope.Current);
        Assert.Null(scopes.UserInput);
    }

    [Fact]
    public void Begin_InteractiveRunExposesTheUserInputScope()
    {
        using var scopes = RunScopeSet.Begin(null, ["ReadFile"], ["lint"], ["style"], interactive: true);

        Assert.NotNull(scopes.UserInput);
        Assert.Same(scopes.UserInput, UserInputScope.Current);
        Assert.True(SkillFilterScope.Current!.IsDisabled("lint"));
        Assert.True(InstructionFilterScope.Current!.IsEnabled("style"));
        Assert.NotNull(ToolFilterScope.Current);
    }

    [Fact]
    public async Task Activate_ReassertsEveryScopeOnAFreshAsyncContext()
    {
        using var workspace = WorkspaceScope.Begin(_root);
        using var scopes = RunScopeSet.Begin(workspace, ["WriteFile"], ["lint"], ["style"], interactive: true);

        // A new task starts with the scopes copied in, so clear them the way a resumed iterator would.
        await Task.Run(() =>
        {
            ClearAll();
            Assert.Null(WorkspaceScope.Current);
            Assert.Null(InstructionFilterScope.Current);

            scopes.Activate();

            Assert.Same(workspace, WorkspaceScope.Current);
            Assert.NotNull(ToolFilterScope.Current);
            Assert.True(SkillFilterScope.Current!.IsDisabled("lint"));
            Assert.True(InstructionFilterScope.Current!.IsEnabled("style"));
            Assert.Same(scopes.UserInput, UserInputScope.Current);
        });
    }

    [Fact]
    public void BuildRunOptions_InjectsEnabledInstructionsAndTheSkillCatalogue()
    {
        using var workspace = WorkspaceScope.Begin(_root);
        using var scopes = RunScopeSet.Begin(workspace, null, disabledSkills: ["lint"], enabledInstructions: ["style"], interactive: false);

        var text = InstructionsOf(RunScopeSet.BuildRunOptions(supportsSkills: true, new SkillLoader(), new InstructionLoader()));

        Assert.NotNull(text);
        Assert.Contains("<customInstructions>", text);
        Assert.Contains("Use tabs.", text);
        Assert.DoesNotContain("Always add a test.", text);
        Assert.Contains("<skills>", text);
        Assert.Contains("- get-date:", text);
        Assert.DoesNotContain("- lint:", text);
    }

    [Fact]
    public void BuildRunOptions_SkipsSkillsForAgentsThatDoNotUseThem()
    {
        using var workspace = WorkspaceScope.Begin(_root);
        using var scopes = RunScopeSet.Begin(workspace, null, null, ["tests"], interactive: false);

        var text = InstructionsOf(RunScopeSet.BuildRunOptions(supportsSkills: false, new SkillLoader(), new InstructionLoader()));

        Assert.Contains("Always add a test.", text);
        Assert.DoesNotContain("<skills>", text);
    }

    [Fact]
    public void BuildRunOptions_ReturnsNullWhenNothingIsInjected()
    {
        using var workspace = WorkspaceScope.Begin(_root);
        using var scopes = RunScopeSet.Begin(workspace, null, null, enabledInstructions: null, interactive: false);

        Assert.Null(RunScopeSet.BuildRunOptions(supportsSkills: false, new SkillLoader(), new InstructionLoader()));
    }

    // Simulates an async iterator resuming after a yield, which resets every AsyncLocal on the context.
    private static void ClearAll()
    {
        using (WorkspaceScope.Begin(Path.GetTempPath())) { }
        using (ToolFilterScope.Begin([])) { }
        using (SkillFilterScope.Begin([])) { }
        using (InstructionFilterScope.Begin([])) { }
        using (UserInputScope.Begin()) { }
    }
}
