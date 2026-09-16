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
            ["model-to-agent", "inside-the-harness", "agent-loop", "wider-ecosystem", "run-and-improve"],
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
        Assert.Equal("/learn?stage=model-to-agent", AgentLearningJourney.Href(AgentLearningJourney.Stages[0].Id));
    }

    /// <summary>Navigation skips hidden content and numbers the final visible stage consecutively.</summary>
    [Fact]
    public void Navigation_SkipsHiddenFoundryStage()
    {
        Assert.Equal("run-and-improve", AgentLearningJourney.Move("wider-ecosystem", 1).Id);
        Assert.Equal("wider-ecosystem", AgentLearningJourney.Move("run-and-improve", -1).Id);
        Assert.Equal(4, AgentLearningJourney.IndexOf("run-and-improve"));
        Assert.All(AgentLearningJourney.Stages, stage => Assert.False(stage.Hidden));
    }

    /// <summary>Each visible lesson selects only its own nodes and is not a product-specific platform mapping.</summary>
    [Theory]
    [InlineData("model-to-agent", "harness,model")]
    [InlineData("inside-the-harness", "instructions,harness-context,available-tools,execution-controls")]
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