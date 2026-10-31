using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class PlanarSpaceContainmentTests
{
    public static IEnumerable<object[]> Frames()
    {
        for (int axis = 0; axis < 3; axis++)
            foreach (bool reverse in new[] { false, true })
                foreach (bool negative in new[] { false, true })
                    yield return [axis, reverse, negative];
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void NearHorizontalCrossing_DoesNotTurnAnInteriorLoopIntoAnIsland(int axis, bool reverse, bool negative)
    {
        double middle = Math.BitIncrement(100d);
        double upper = Math.BitIncrement(middle);
        List<double[]> outer = [[0, 0, 0], [100, 0, 0], [100, 100, 0],
            [101, upper, 0], [100, 200, 0], [0, 200, 0]];
        List<double[]> hole = [[40, middle, 0], [60, middle, 0], [60, 110, 0], [40, 110, 0]];
        foreach (var ring in new[] { outer, hole })
        {
            foreach (var p in ring)
            {
                if (negative) { p[0] = -p[0]; p[1] = -p[1]; }
                if (axis != 2) (p[axis], p[2]) = (p[2], p[axis]);
            }
            if (reverse) ring.Reverse();
        }
        var before = outer.Concat(hole).Select(p => p.ToArray()).ToArray();
        if (axis == 2 && !reverse && !negative)
            Assert.Equal(2, GRepParserV2.ClassifyCoplanarRings([outer, hole]).Count);
        var regions = GRepParserV2.ClassifyCoplanarRings([outer, hole], exactPredicates: true);
        Assert.Single(regions);
        Assert.Single(regions[0].Holes);
        Assert.All(outer.Concat(hole), p => Assert.Contains(before, q => p.SequenceEqual(q)));
    }

    [Fact]
    public void SubnormalCrossing_IsNotRoundedToHorizontal()
    {
        List<double[]> outer = [[0, -100, 0], [100, -100, 0], [100, 0, 0],
            [101, double.Epsilon * 2, 0], [100, 100, 0], [0, 100, 0]];
        List<double[]> hole = [[40, double.Epsilon, 0], [60, double.Epsilon, 0], [60, 10, 0], [40, 10, 0]];
        var regions = GRepParserV2.ClassifyCoplanarRings([outer, hole], exactPredicates: true);
        Assert.Single(regions);
        Assert.Single(regions[0].Holes);
    }

    [Theory]
    [InlineData("touch")]
    [InlineData("outside-ulp")]
    [InlineData("disjoint")]
    public void NonContainedLoops_AreNotMadeIntoHoles(string kind)
    {
        List<double[]> outer = [[0, 0, 0], [100, 0, 0], [100, 100, 0], [0, 100, 0]];
        double end = kind == "outside-ulp" ? Math.BitIncrement(100d) : kind == "disjoint" ? 120 : 100;
        List<double[]> other = [[90, 40, 0], [end, 40, 0], [end, 60, 0], [90, 60, 0]];
        var regions = GRepParserV2.ClassifyCoplanarRings([outer, other], exactPredicates: true);
        Assert.Equal(2, regions.Count);
        Assert.All(regions, r => Assert.Empty(r.Holes));
    }

    [Fact]
    public void NestedIslandAndSeparateRegion_RetainTheirTopologyBeyondDecimalRange()
    {
        List<double[]> Square(double start, double end) =>
            [[start * 1e30, start, 0], [end * 1e30, start, 0],
             [end * 1e30, end, 0], [start * 1e30, end, 0]];
        var regions = GRepParserV2.ClassifyCoplanarRings(
            [Square(0, 100), Square(20, 80), Square(30, 70), Square(200, 220)], exactPredicates: true);
        Assert.Equal(3, regions.Count);
        Assert.Single(regions.SelectMany(r => r.Holes));
    }
}
