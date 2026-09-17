using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TheSeries.Web;
using TheSeries.Web.Learning;
using Xunit;

namespace TheSeries.Web.Tests;

/// <summary>Protects journey permalinks, navigation and references to the shipped learning content.</summary>
public sealed class AgentLearningJourneyTests
{
    /// <summary>Visible stages retain their identifiers and exclude the product-specific lesson.</summary>
    [Fact]
    public void Stages_PreserveOrderedPermalinks()
    {
        Assert.Equal(
            ["model-to-agent", "agent-landscape", "inside-the-harness", "anatomy-of-agent", "agent-loop", "agents-everywhere", "wider-ecosystem", "run-and-improve"],
            AgentLearningJourney.Stages.Select(stage => stage.Id).ToArray());
    }

    /// <summary>Missing and retired links always resolve to a usable first stage.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("retired-stage")]
    [InlineData("map-to-foundry")]
    [InlineData("MAP-TO-FOUNDRY")]
    public void Resolve_FallsBackToFirstStage(string? id)
    {
        Assert.Same(AgentLearningJourney.Stages[0], AgentLearningJourney.Resolve(id));
    }

    /// <summary>Query-string identifiers accept mixed casing.</summary>
    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.Equal("agent-loop", AgentLearningJourney.Resolve("AGENT-LOOP").Id);
    }

    /// <summary>Adjacent and extreme moves stay within the ordered journey.</summary>
    [Fact]
    public void Move_NavigatesEveryStageAndClampsBoundaries()
    {
        var stages = AgentLearningJourney.Stages;
        for (var index = 0; index < stages.Count; index++)
        {
            Assert.Same(stages[Math.Max(0, index - 1)], AgentLearningJourney.Move(stages[index].Id, -1));
            Assert.Same(stages[Math.Min(stages.Count - 1, index + 1)], AgentLearningJourney.Move(stages[index].Id, 1));
        }

        Assert.Same(stages[0], AgentLearningJourney.Move(stages[^1].Id, int.MinValue));
        Assert.Same(stages[^1], AgentLearningJourney.Move(stages[^1].Id, int.MaxValue));
    }

    /// <summary>Every stage links to readable concepts and defined architecture nodes.</summary>
    [Fact]
    public void Stages_ReferenceExistingConceptsAndNodes()
    {
        var catalog = new ConceptCatalog(new ContentEnvironment(), NullLogger<ConceptCatalog>.Instance);
        Assert.NotEmpty(catalog.All);
        Assert.Equal(AgentLearningJourney.Nodes.Count, AgentLearningJourney.Nodes.Select(node => node.Id).Distinct().Count());

        foreach (var stage in AgentLearningJourney.Stages)
        {
            Assert.NotEmpty(stage.ConceptIds);
            Assert.NotEmpty(stage.HighlightedNodes);
            Assert.All(stage.ConceptIds, id =>
            {
                var concept = catalog.Get(id);
                Assert.NotNull(concept);
                Assert.False(string.IsNullOrWhiteSpace(concept.BodyHtml));
            });
            Assert.All(stage.HighlightedNodes, id => Assert.Contains(AgentLearningJourney.Nodes, node => node.Id == id));
        }
    }

    /// <summary>Demo actions are limited to existing local pages and stage URLs remain predictable.</summary>
    [Fact]
    public void DemoActions_OnlyNavigateToExistingLocalPages()
    {
        foreach (var stage in AgentLearningJourney.Stages)
        {
            if (stage.ActionHref is not null)
            {
                Assert.Contains(stage.ActionHref, new[] { "/", "/discovery" });
                Assert.False(string.IsNullOrWhiteSpace(stage.ActionLabel));
            }

            Assert.Equal($"/learn?stage={stage.Id}", AgentLearningJourney.Href(stage.Id));
        }
    }

    /// <summary>The harness lesson contains only its four responsibilities and matching related concepts.</summary>
    [Fact]
    public void HarnessStage_FocusesOnResponsibilities()
    {
        var stage = AgentLearningJourney.Resolve("inside-the-harness");

        Assert.Equal(["instructions", "harness-context", "available-tools", "execution-controls"], stage.HighlightedNodes.ToArray());
        Assert.Equal(["system-prompt", "context", "tools", "guardrails"], stage.ConceptIds.ToArray());
        Assert.False(stage.PlatformMap);
    }

    /// <summary>The loop lesson distinguishes decisions, execution, observations and the answer exit.</summary>
    [Fact]
    public void LoopStage_FocusesOnExecutionCycle()
    {
        var stage = AgentLearningJourney.Resolve("agent-loop");

        Assert.Equal(["loop-context", "loop-decision", "loop-execute", "loop-observe", "loop-answer"], stage.HighlightedNodes.ToArray());
        Assert.Equal(["reasoning", "tools", "context"], stage.ConceptIds.ToArray());
        Assert.Equal("/", stage.ActionHref);
        Assert.False(stage.PlatformMap);
    }

    /// <summary>The introductory lesson uses its concise title while preserving existing bookmarks.</summary>
    [Fact]
    public void AgentStage_PreservesPermalinkWithConciseTitle()
    {
        Assert.Equal("Agent", AgentLearningJourney.Resolve("model-to-agent").Title);
        Assert.Equal("/learn?stage=model-to-agent", AgentLearningJourney.Href(AgentLearningJourney.Resolve("model-to-agent").Id));
    }

    /// <summary>Navigation skips hidden content and numbers the final visible stage consecutively.</summary>
    [Fact]
    public void Navigation_SkipsHiddenFoundryStage()
    {
        Assert.Equal("run-and-improve", AgentLearningJourney.Move("wider-ecosystem", 1).Id);
        Assert.Equal("wider-ecosystem", AgentLearningJourney.Move("run-and-improve", -1).Id);
        Assert.Equal(7, AgentLearningJourney.IndexOf("run-and-improve"));
        Assert.All(AgentLearningJourney.Stages, stage => Assert.False(stage.Hidden));
    }

    /// <summary>Each visible lesson selects only its own nodes and is not a product-specific platform mapping.</summary>
    [Theory]
    [InlineData("agent-landscape", "harness,model")]
    [InlineData("agents-everywhere", "harness,model")]
    [InlineData("model-to-agent", "harness,model")]
    [InlineData("inside-the-harness", "instructions,harness-context,available-tools,execution-controls")]
    [InlineData("anatomy-of-agent", "instructions,available-tools,anatomy-persona,anatomy-selected-tools,anatomy-settings,anatomy-task,anatomy-custom-instructions,anatomy-skills")]
    [InlineData("agent-loop", "loop-context,loop-decision,loop-execute,loop-observe,loop-answer")]
    [InlineData("wider-ecosystem", "connected-agent,mcp-tools,a2a-agent")]
    [InlineData("run-and-improve", "lifecycle-run,lifecycle-observe,lifecycle-evaluate,lifecycle-improve")]
    public void Stage_SelectsOnlyItsFocusedNodes(string stageId, string expectedNodeIds)
    {
        var expectedNodes = expectedNodeIds.Split(',');
        var stage = AgentLearningJourney.Resolve(stageId);
        Assert.Equal(expectedNodes, stage.HighlightedNodes.ToArray());
        Assert.False(stage.PlatformMap);
        Assert.InRange(stage.ConceptIds.Count, 1, 4);
    }

    /// <summary>Reveal navigation is bounded, reversible and reset only when the lesson changes.</summary>
    [Fact]
    public void FoundationStory_NavigationPreservesAndResetsProgress()
    {
        var story = new FoundationStory();
        foreach (var stageId in new[] { "agent-landscape", "model-to-agent", "agents-everywhere", "anatomy-of-agent" })
        {
            story.SetStage(stageId);
            Assert.Equal(0, story.Beat);
            Assert.False(story.CanPrevious);
            story.Move(int.MaxValue);
            Assert.Equal(story.Captions.Count - 1, story.Beat);
            Assert.False(story.CanNext);
            story.SetStage(stageId);
            Assert.Equal(story.Captions.Count - 1, story.Beat);
            story.Move(-1);
            Assert.True(story.CanNext);
            story.Complete();
            Assert.False(story.CanNext);
            story.Move(int.MinValue);
            Assert.Equal(0, story.Beat);
            story.Complete();
            story.Restart();
            Assert.Equal(0, story.Beat);
        }
    }

    /// <summary>Composition separates the outbound model request from its response before revealing the agent boundary.</summary>
    [Fact]
    public void FoundationStory_CompositionSeparatesRequestAndResponse()
    {
        var story = new FoundationStory();
        story.SetStage("model-to-agent");

        Assert.Equal(7, story.Captions.Count);
        Assert.Contains("Start with the two parts: Application + Model", story.Caption);
        story.Move(1);
        Assert.Contains("The application's harness assembles instructions and context", story.Caption);
        story.Move(1);
        Assert.Contains("The application executes tools", story.Caption);
        story.Move(1);
        Assert.Contains("A model generates a response", story.Caption);
        story.Move(1);
        Assert.Contains("sends context, available tool definitions and any previous tool results", story.Caption);
        story.Move(1);
        Assert.Contains("model returns an answer or a tool request", story.Caption);
        Assert.True(story.CanNext);
        story.Move(1);
        Assert.Contains("Application plus model forms the agent", story.Caption);
        Assert.False(story.CanNext);
    }

    /// <summary>Task and trigger selection are independent of presentation progress and each other.</summary>
    [Fact]
    public void FoundationStory_ExamplesAndTriggersAreIndependent()
    {
        var story = new FoundationStory();
        story.SetStage("agents-everywhere");
        story.Complete();
        foreach (var example in FoundationStory.Examples)
        {
            story.SelectExample(example.Id);
            Assert.Same(example, story.Example);
            Assert.All(new[] { example.Task, example.Context, example.LocalTools, example.CloudTools,
                example.LocalControl, example.CloudControl, example.ApplicationExamples }, value => Assert.False(string.IsNullOrWhiteSpace(value)));
            foreach (var trigger in FoundationStory.Triggers)
            {
                story.SelectTrigger(trigger);
                Assert.Equal(trigger, story.Trigger);
                Assert.Same(example, story.Example);
                Assert.False(story.CanNext);
            }
        }
        var selected = story.Example;
        story.SelectExample("unknown");
        story.SelectTrigger("unknown");
        Assert.Same(selected, story.Example);
        Assert.Equal("Event", story.Trigger);
    }

    /// <summary>Anatomy is a new addressable chapter between harness responsibilities and execution.</summary>
    [Fact]
    public void AnatomyStage_HasStablePlacementAndPermalink()
    {
        Assert.Equal("anatomy-of-agent", AgentLearningJourney.Move("inside-the-harness", 1).Id);
        Assert.Equal("agent-loop", AgentLearningJourney.Move("anatomy-of-agent", 1).Id);
        Assert.Equal("anatomy-of-agent", AgentLearningJourney.Move("agent-loop", -1).Id);
        Assert.Equal("/learn?stage=anatomy-of-agent", AgentLearningJourney.Href(AgentLearningJourney.Resolve("ANATOMY-OF-AGENT").Id));
    }

    /// <summary>Example selection preserves reveals while chapter reentry restores the initial example.</summary>
    [Fact]
    public void AnatomyStory_SeparatesSelectionFromRevealProgress()
    {
        var story = new FoundationStory();
        story.SetStage("anatomy-of-agent");
        Assert.Equal(8, story.Captions.Count);
        Assert.Equal("ask", story.Anatomy.Id);
        story.Move(2);
        foreach (var example in FoundationStory.AnatomyExamples)
        {
            story.SelectAnatomy(example.Id);
            Assert.Same(example, story.Anatomy);
            Assert.Equal(2, story.Beat);
            Assert.Contains(story.LoadedSkill, example.Skills);
        }
        story.SelectAnatomy("unknown");
        Assert.Equal("review", story.Anatomy.Id);
        story.Complete();
        Assert.Equal(7, story.Beat);
        Assert.Contains("Read skill", story.Caption);
        story.SetStage("anatomy-of-agent");
        Assert.Equal(7, story.Beat);
        Assert.Equal("review", story.Anatomy.Id);
        story.Move(-1);
        Assert.Equal(6, story.Beat);
        story.Restart();
        Assert.Equal(0, story.Beat);
        Assert.Equal("review", story.Anatomy.Id);
        story.SetStage("agent-loop");
        story.SetStage("anatomy-of-agent");
        Assert.Equal(0, story.Beat);
        Assert.Equal("ask", story.Anatomy.Id);
    }

    /// <summary>Illustrative read-only subsets stay within the shared catalogue and can load their skills.</summary>
    [Fact]
    public void AnatomyExamples_UseBoundedCapabilitiesAndCompletePlaybooks()
    {
        var capabilityIds = FoundationStory.AnatomyCapabilities.Select(tool => tool.Id).ToArray();
        Assert.Equal(capabilityIds.Length, capabilityIds.Distinct().Count());
        Assert.Equal(["ask", "plan", "review"], FoundationStory.AnatomyExamples.Select(example => example.Id).ToArray());
        foreach (var example in FoundationStory.AnatomyExamples)
        {
            Assert.All(example.ToolIds, id => Assert.Contains(id, capabilityIds));
            Assert.Contains("skill", example.ToolIds);
            Assert.DoesNotContain("write", example.ToolIds);
            Assert.DoesNotContain("terminal", example.ToolIds);
            Assert.DoesNotContain("delegate", example.ToolIds);
            Assert.False(string.IsNullOrWhiteSpace(example.Persona));
            Assert.False(string.IsNullOrWhiteSpace(example.Task));
            Assert.Equal(2, example.Skills.Count);
            Assert.All(example.Skills, skill =>
            {
                Assert.False(string.IsNullOrWhiteSpace(skill.Name));
                Assert.False(string.IsNullOrWhiteSpace(skill.Description));
                Assert.Equal(3, skill.Steps.Count);
                Assert.All(skill.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step)));
            });
        }
        Assert.Contains("ask", FoundationStory.AnatomyExamples[1].ToolIds);
        Assert.Contains("docs", FoundationStory.AnatomyExamples[2].ToolIds);
        Assert.DoesNotContain("docs", FoundationStory.AnatomyExamples[0].ToolIds);
    }

    /// <summary>Purpose selection changes every anatomy layer without revealing later content.</summary>
    [Fact]
    public void AnatomyPurposes_ProvideCoherentConfigurationsWithoutResettingReveals()
    {
        var story = new FoundationStory();
        story.SetStage("anatomy-of-agent");
        story.Move(2);
        Assert.Equal(["chat", "office", "coding", "custom"], FoundationStory.AnatomyPurposes.Select(profile => profile.Id).ToArray());
        foreach (var profile in FoundationStory.AnatomyPurposes)
        {
            story.SelectAnatomyPurpose(profile.Id);
            Assert.Same(profile, story.AnatomyProfile);
            Assert.Same(profile.Examples[0], story.Anatomy);
            Assert.Equal(2, story.Beat);
            Assert.All(new[] { profile.SystemPrompt, profile.Model, profile.Controls, profile.Instructions, profile.ContextLabel },
                value => Assert.False(string.IsNullOrWhiteSpace(value)));
            var ids = profile.Capabilities.Select(tool => tool.Id).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
            foreach (var example in profile.Examples)
            {
                story.SelectAnatomy(example.Id);
                Assert.Same(example, story.Anatomy);
                Assert.All(example.ToolIds, id => Assert.Contains(id, ids));
                Assert.Contains("skill", example.ToolIds);
                Assert.Contains(story.LoadedSkill, example.Skills);
                Assert.Equal(2, example.Skills.Count);
                Assert.All(example.Skills, skill => Assert.Equal(3, skill.Steps.Count));
                Assert.Equal(2, story.Beat);
            }
        }
        story.SelectAnatomyPurpose("office");
        Assert.Contains("send", story.Anatomy.ToolIds);
        Assert.Contains("confirmation", story.AnatomyProfile.Controls);
        Assert.DoesNotContain("delete", story.Anatomy.ToolIds);
        story.SelectAnatomy("review");
        story.SelectAnatomyPurpose("unknown");
        Assert.Equal("office-agent", story.Anatomy.Id);
        story.Complete();
        story.Restart();
        Assert.Equal("office", story.AnatomyProfile.Id);
        Assert.Equal(0, story.Beat);
        story.SetStage("agents-everywhere");
        story.SetStage("anatomy-of-agent");
        Assert.Equal("coding", story.AnatomyProfile.Id);
        Assert.Equal("ask", story.Anatomy.Id);
        story.SelectAnatomyPurpose("custom");
        Assert.DoesNotContain("control", story.Anatomy.ToolIds);
        Assert.DoesNotContain("delegate", story.Anatomy.ToolIds);
    }

    private sealed class ContentEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TheSeries.Web.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}