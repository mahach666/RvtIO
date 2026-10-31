using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class GElementChainRayTests
{
    [Fact]
    public void UnskippedPartitionCountsWithinEdgesAndConnectorsWithoutSeparateChainClosures()
    {
        var value = Read(Loop(), [Range(0, 2, 0, 0, 2, 2), Range(2, 4, 0, 0, 2, 2)], Bits(1, 1));
        Assert.True(value.IsInside); Assert.Equal(1, value.Crossings);
        Assert.Equal(8, value.Chains.Sum(c => c.Segments.Count));
        Assert.All(value.Chains, c => { Assert.False(c.Skipped); Assert.False(c.StartsWithPreviousEndpoint); });
    }

    [Fact]
    public void SkippedPreviousChainSeedsNextRayWithPreviousEdgeEnd()
    {
        var value = Read(Loop(), [Range(0, 2, 10, 0, 12, 2), Range(2, 4, 0, 0, 2, 2)], Bits(1, 1));
        Assert.True(value.Chains[0].Skipped); Assert.True(value.Chains[1].StartsWithPreviousEndpoint);
        Assert.Equal(5, value.Chains[1].Segments.Count);
        Assert.Equal(11, value.Chains[1].Segments[0].From.EdgeQueueIndex);
        Assert.Equal(12, value.Chains[1].Segments[0].To.EdgeQueueIndex);
    }

    [Fact]
    public void LastSkippedYEnvelopeMustProcessWhenOppositeToFirstForClosingConnector()
    {
        var value = Read(Loop(), [Range(0, 2, 0, 2, 2, 3), Range(2, 4, 0, -2, 2, -1)], Bits(1, 0));
        Assert.True(value.Chains[0].Skipped); Assert.False(value.Chains[1].Skipped);
        Assert.True(value.Chains[1].StartsWithPreviousEndpoint);
    }

    [Fact]
    public void PreviousSkippedYEnvelopeCanOverrideSkipBeforeLastChain()
    {
        var value = Read(Loop(), [Range(0, 1, 0, 2, 2, 3), Range(1, 2, 0, -2, 2, -1), Range(2, 4, 0, -2, 2, 3)], Bits(1, 0));
        Assert.True(value.Chains[0].Skipped); Assert.False(value.Chains[1].Skipped);
        Assert.False(value.Chains[2].StartsWithPreviousEndpoint);
    }

    [Theory]
    [InlineData(0, 6)] [InlineData(0, 7)] [InlineData(1, 6)] [InlineData(1, 7)]
    public void EndpointOrdinalsFollowNativeSideAndFlag(int side, byte flags)
    {
        var loop = Loop(); loop = loop with { Edges = loop.Edges.Select(e => e with { FaceSide = side, Flags = flags }).ToArray() };
        var value = Read(loop, [Range(0, 4, 0, 0, 2, 2)], Bits(1, 1));
        bool reverse = (side == 1) != ((flags & 1) != 0);
        Assert.Equal(reverse ? 1 : 0, value.Chains[0].Segments[0].From.PointOrdinal);
        Assert.Equal(reverse ? 0 : 1, value.Chains[0].Segments[0].To.PointOrdinal);
    }

    [Theory]
    [InlineData("empty")] [InlineData("gap")] [InlineData("overlap")]
    [InlineData("envelope")] [InlineData("query")] [InlineData("topology")]
    public void UnqualifiedInputsFailExplicitly(string change)
    {
        var loop = Loop(); SourceChainRange[] ranges = [Range(0, 4, 0, 0, 2, 2)]; var query = Bits(1, 1);
        switch (change)
        {
            case "empty": ranges = []; break;
            case "gap": ranges = [Range(1, 4, 0, 0, 2, 2)]; break;
            case "overlap": ranges = [Range(0, 3, 0, 0, 2, 2), Range(2, 4, 0, 0, 2, 2)]; break;
            case "envelope": ranges = [Range(0, 4, 2, 0, 1, 2)]; break;
            case "query": query = Bits(double.NaN, 1); break;
            default: loop = loop with { EndOutcome = "null-next" }; break;
        }
        Assert.Null(EvaluateSourceTwoPointChainRay(loop, ranges, query, out var why)); Assert.Equal("two-point-chain-ray", why);
    }

    static GElementLoopTopology Loop()
    {
        double[][] ps = [[0, 0], [2, 0], [2, 2], [0, 2]];
        return new(1, 1257, 0, null, 10, 13, 0, true, [], 10, ps.Select((p, i) =>
            new GElementLoopEdgeVisit(10 + i, 0, 6, 2,
                [new(100, 132, Bits(p), Bits(p)), new(132, 164, Bits(ps[(i + 1) % 4]), Bits(ps[(i + 1) % 4]))],
                [], i == 3 ? 1 : 11 + i, "loop-sentinel")).ToArray(), "loop-sentinel");
    }
    static SourceChainRange Range(int a, int b, params double[] e) => new(a, b, Bits(e));
    static long[] Bits(params double[] v) => v.Select(BitConverter.DoubleToInt64Bits).ToArray();
    static SourceChainRay Read(GElementLoopTopology loop, IReadOnlyList<SourceChainRange> ranges, long[] q)
    { var value = EvaluateSourceTwoPointChainRay(loop, ranges, q, out var why); Assert.Null(why); Assert.NotNull(value); return value; }
}
