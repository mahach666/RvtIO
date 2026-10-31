using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class GElementPolygonCrossingsTests
{
    [Theory]
    [InlineData(1, 1, 1, true)]
    [InlineData(-1, 1, 0, false)]
    [InlineData(3, 1, 2, false)]
    [InlineData(1, -1, 0, false)]
    [InlineData(1, 3, 0, false)]
    public void LiteralLeftRayCountsCrossings(double x, double y, int count, bool inside)
    {
        var value = Read(Square(), x, y);
        Assert.Equal(count, value.Crossings); Assert.Equal(inside, value.IsInside);
    }

    [Theory]
    [InlineData(1, 0, false)] [InlineData(1, 2, true)]
    [InlineData(0, 1, false)] [InlineData(2, 1, true)]
    public void RawPolygonEqualityIsNotAnEpsilonBoundaryVerdict(double x, double y, bool inside)
        => Assert.Equal(inside, Read(Square(), x, y).IsInside);

    [Fact]
    public void WindingReversalRetainsParityButLiteralEqualitySidesRemainSourceBound()
    {
        var points = Square();
        Assert.Equal(Read(points, 1, 1).IsInside, Read(points.AsEnumerable().Reverse().ToArray(), 1, 1).IsInside);
        Assert.False(Read(points.AsEnumerable().Reverse().ToArray(), 1, 0).IsInside);
        Assert.True(Read(points.AsEnumerable().Reverse().ToArray(), 1, 2).IsInside);
    }

    [Fact]
    public void HorizontalAndRepeatedVerticesDoNotDivideByZeroOrGetDeduplicated()
    {
        IReadOnlyList<IReadOnlyList<long>> points = [Bits(0, 0), Bits(0, 0), Bits(2, 0), Bits(2, 2), Bits(0, 2), Bits(0, 0)];
        var value = Read(points, 1, 1);
        Assert.Equal(6, value.IntersectionXBits.Count); Assert.Equal(1, value.Crossings);
        Assert.Equal(2, value.IntersectionXBits.Count(v => v != null));
    }

    [Fact]
    public void SingleVertexHasZeroCrossings()
    {
        var value = Read([Bits(1, 1)], 1, 1);
        Assert.False(value.IsInside); Assert.Null(Assert.Single(value.IntersectionXBits));
    }

    [Fact]
    public void SelfIntersectingSequenceUsesParityNotSignedArea()
    {
        IReadOnlyList<IReadOnlyList<long>> points = [Bits(0, 0), Bits(2, 2), Bits(0, 2), Bits(2, 0)];
        Assert.True(Read(points, 1, 0.5).IsInside);
        Assert.True(Read(points, 1, 1.5).IsInside);
        Assert.False(Read(points, 3, 1).IsInside);
    }

    [Fact]
    public void IntersectionKeepsOriginalMultiplyDivideAddOrderAsBits()
    {
        IReadOnlyList<IReadOnlyList<long>> points = [Bits(1.25, -3.5), Bits(8.75, 9.5), Bits(-4.25, 9.5)];
        double expected = ((1.25 - 8.75) * (2.75 - (-3.5))) / (-3.5 - 9.5) + 1.25;
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), Read(points, 3, 2.75).IntersectionXBits[0]);
    }

    [Theory]
    [InlineData("empty")] [InlineData("query-width")] [InlineData("point-width")]
    [InlineData("query-nan")] [InlineData("point-infinity")] [InlineData("overflow")]
    public void UnsupportedInputsFailExplicitly(string kind)
    {
        IReadOnlyList<IReadOnlyList<long>> points = Square(); var query = Bits(1, 1);
        switch (kind)
        {
            case "empty": points = []; break;
            case "query-width": query = [0]; break;
            case "point-width": points = [[0]]; break;
            case "query-nan": query = Bits(double.NaN, 1); break;
            case "point-infinity": points = [Bits(double.PositiveInfinity, 0)]; break;
            default: points = [Bits(double.MaxValue, -1), Bits(-double.MaxValue, 1)]; break;
        }
        Assert.Null(EvaluateSourcePolygonCrossings(points, query, out var why));
        Assert.Equal("polygon-crossings", why);
    }

    static IReadOnlyList<IReadOnlyList<long>> Square() => [Bits(0, 0), Bits(2, 0), Bits(2, 2), Bits(0, 2)];
    static long[] Bits(params double[] values) => values.Select(BitConverter.DoubleToInt64Bits).ToArray();
    static SourcePolygonCrossings Read(IReadOnlyList<IReadOnlyList<long>> points, double x, double y)
    {
        var value = EvaluateSourcePolygonCrossings(points, Bits(x, y), out var why);
        Assert.Null(why); Assert.NotNull(value); return value;
    }
}
