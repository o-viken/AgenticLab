using AgenticLab.Web.Flow;
using Xunit;
using static AgenticLab.Web.Flow.NetworkSimulation;

namespace AgenticLab.Web.Tests;

public sealed class NetworkSimulationTests
{
    private static readonly SimToken Token = new("hello", 0.6,
    [
        new TokenCandidate("hi", 0.25),
        new TokenCandidate("hello", 0.6),
        new TokenCandidate("hey", 0.1),
    ]);

    [Fact]
    public void Ordered_PutsThePickedTokenFirst()
    {
        var ordered = Ordered(Token);
        Assert.Equal(["hello", "hi", "hey"], ordered.Select(c => c.Text));
        Assert.Equal(5, RestPercent(ordered));
    }

    [Fact]
    public void OutputSlots_PlaceEveryCandidateOnceAndAreStablePerToken()
    {
        var slots = OutputSlots("hello", LayerSizes[^1], 3);

        Assert.Equal(LayerSizes[^1], slots.Length);
        Assert.Equal([0, 1, 2], slots.Where(s => s >= 0).OrderBy(s => s));
        Assert.Equal(slots, OutputSlots("hello", LayerSizes[^1], 3));
        Assert.NotEqual(slots, OutputSlots("world", LayerSizes[^1], 3));
    }

    [Fact]
    public void InputVectors_FillTheInputLayerAndStayInRange()
    {
        var live = LiveInputVector("hello");
        Assert.Equal(LayerSizes[0], live.Length);
        Assert.All(live, v => Assert.InRange(v, -1.0, 1.0));
        Assert.Equal(live, LiveInputVector("hello"));

        var pinned = PinnedInputVector(new EmbeddingToken("x", [0.5, -0.25], 0, 0));
        Assert.Equal(LayerSizes[0], pinned.Length);
        Assert.Equal(0.5, pinned[0]);
        Assert.Equal(-0.25, pinned[1]);
        Assert.All(pinned.Skip(2), v => Assert.Equal(0.0, v));
    }

    [Fact]
    public void NodeRadius_GrowsWithMagnitudeAndProbabilityAndRestsOtherwise()
    {
        var ordered = Ordered(Token);
        Assert.Equal(4.0, NodeRadius(isInput: true, 0, -1, null, null));
        Assert.True(NodeRadius(isInput: true, 0, -1, null, [0.9]) > NodeRadius(isInput: true, 0, -1, null, [0.1]));
        Assert.True(NodeRadius(isInput: false, 0, 0, ordered, null) > NodeRadius(isInput: false, 0, 2, ordered, null));
        Assert.Equal(4.0, NodeRadius(isInput: false, 0, -1, ordered, null));
    }

    [Fact]
    public void Geometry_KeepsNodesInsideTheViewBox()
    {
        for (var layer = 0; layer < LayerSizes.Length; layer++)
        {
            Assert.InRange(LayerX(layer), 0, VbW);
            for (var i = 0; i < LayerSizes[layer]; i++)
            {
                Assert.InRange(NodeY(LayerSizes[layer], i), 0, VbH);
            }
        }

        Assert.Equal(LayerSizes[0], InputValueLabels(LiveInputVector("hello")).Split("<text ").Length - 1);
    }

    [Theory]
    [InlineData("\n", "↵")]
    [InlineData("  ", "␣")]
    [InlineData("word", "word")]
    [InlineData("", "")]
    public void Display_MakesWhitespaceVisible(string text, string expected) => Assert.Equal(expected, Display(text));
}
