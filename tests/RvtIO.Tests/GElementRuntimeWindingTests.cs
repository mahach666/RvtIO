using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class GElementRuntimeWindingTests
{
    public static IEnumerable<object?[]> StatePairs()
    {
        int?[] values = [null, -1, 0, 1, 2];
        foreach (var unrefined in values) foreach (var refined in values) yield return [unrefined, refined];
    }

    [Theory]
    [MemberData(nameof(StatePairs))]
    public void AllRuntimeFlagPartitionsMustAgreeWithDirectOutside(int? unrefined, int? refined)
    {
        var value = CompareSourceWindingRuntimeModes(unrefined, refined); Assert.Equal(4, value.Modes.Count);
        Assert.Equal(0, value.Modes[0].PointWithinState); Assert.Equal("direct-outside", value.Modes[0].Path);
        Assert.Equal(refined, value.Modes[1].PointWithinState); Assert.Equal(unrefined, value.Modes[2].PointWithinState);
        Assert.Equal(refined, value.Modes[3].PointWithinState);
        Assert.Equal(unrefined == 0 && refined == 0 ? 0 : (int?)null, value.IndependentState);
        Assert.Equal(new[] { (false, false), (false, true), (true, false), (true, true) },
            value.Modes.Select(m => (m.Bcbc00Nonzero, m.Bcbc01Nonzero)));
    }

    [Fact]
    public void EmptyOwnGraphKeepsSeparatePriorAndExplicitAuthorityLimits()
    {
        var graph = EmptyGraph(); var prior = SelectOwnGElementPlaneWindingFallback(graph, 1, out _);
        var result = SelectOwnGElementRuntimeWinding(graph, 1, out var why);
        Assert.Null(why); Assert.NotNull(result); Assert.NotNull(prior);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(prior.PriorFirstSample),
            System.Text.Json.JsonSerializer.Serialize(result.Prior.PriorFirstSample));
        Assert.Empty(result.Fallbacks); Assert.True(result.FirstSample.Complete);
        Assert.False(result.ActualEpsilonAuthorityClosed); Assert.False(result.CurrentGeometryAuthorityClosed);
    }

    [Fact]
    public void InvalidOwnerAndDuplicateQueueDoNotProduceSuccessfulGraph()
    {
        var graph = EmptyGraph();
        Assert.Null(SelectOwnGElementRuntimeWinding(graph with { OwnerId = -1 }, 1, out var ownerFailure));
        Assert.NotNull(ownerFailure);
        Assert.Null(SelectOwnGElementRuntimeWinding(graph with { Nodes = [graph.Nodes[0], graph.Nodes[0]] }, 1, out var duplicateFailure));
        Assert.NotNull(duplicateFailure);
    }

    static OwnGElementGeometryGraph EmptyGraph() => new(100, 0, [new(-1, 2007, 100, 0, 8, [])], [], 8);
}
