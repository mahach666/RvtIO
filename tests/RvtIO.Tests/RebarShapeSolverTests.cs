using System.Text;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The centre line of a shape driven bar rebuilt from its shape (<see cref="RebarShapeSolver"/>), checked against the dimension
/// rules of the class (outer dimensions, half a diameter inwards, bend radius from the bar type) worked out by hand; the numbers are
/// millimetres (the solver does not care about the unit), bar diameter 8, bend radius 29 (stirrup, bend diameter 50) or 44 (standard, 80).</summary>
public class RebarShapeSolverTests
{
    private const double D = 8;

    private static void Near(double expected, double actual, double tolerance = 1e-9) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, was {actual}");

    private static double[] Start(RebarSegment s) => [s.Origin[0] + s.Axis[0] * s.Start, s.Origin[1] + s.Axis[1] * s.Start, s.Origin[2] + s.Axis[2] * s.Start];

    private static double[] End(RebarSegment s) => [s.Origin[0] + s.Axis[0] * s.End, s.Origin[1] + s.Axis[1] * s.End, s.Origin[2] + s.Axis[2] * s.End];

    [Fact]
    public void StraightBar_IsOneLineAlongTheEdgeFromTheOrigin()
    {
        var pieces = RebarShapeSolver.Solve([[-1, 0, 0]], [16903.5], [0, 0], D, 29, null, null)!;
        var line = Assert.Single(pieces);
        Assert.Equal([-1, 0, 0], line.Axis);
        Near(16903.5, line.End - line.Start);
        Assert.Equal([0, 0, 0], Start(line));
    }

    [Fact]
    public void OpenStirrup_RunsHalfADiameterInsideTheOuterDimensionsWithQuarterArcsAtTheCorners()
    {
        // three edges going down, right, up, all turning left; outer dimensions 579.8, 225.3, 579.8
        var pieces = RebarShapeSolver.Solve([[0, -1, 0], [1, 0, 0], [0, 1, 0]], [579.8, 225.3, 579.8], [0, 1, 1, 0], D, 29, null, null)!;
        Assert.Equal(5, pieces.Count);
        Near(4, Start(pieces[0])[0]);
        Near(-(579.8 - 29 - 4), End(pieces[0])[1]);
        Assert.True(pieces[1].IsArc);
        Near(29, pieces[1].Radius);
        Near(Math.PI / 2, pieces[1].End - pieces[1].Start);
        Assert.Equal([33, -(579.8 - 33), 0], pieces[1].Origin.Select(v => Math.Round(v, 9)).ToArray());
        Near(-(579.8 - 4), Start(pieces[2])[1]);
        Near(225.3 - 33, End(pieces[2])[0]);
        Near(225.3 - 4, Start(pieces[4])[0]);
        Near(0, End(pieces[4])[1]);
        Assert.All(pieces, p => Near(0, Start(p)[2]));
    }

    [Fact]
    public void HookedBar_HasAnArcOfTheHookAngleAndALegOfAMultipleOfTheDiameterAtEachEnd()
    {
        // 135 degree hooks, bend radius 29, leg 6 diameters, on a single edge of 4921.35 going left
        var hook = new RebarShapeSolver.Hook(3 * Math.PI / 4, 29, -1, null, 6);
        var pieces = RebarShapeSolver.Solve([[-1, 0, 0]], [4921.35], [0, 0], D, 29, hook, hook with { Side = -1 })!;
        Assert.Equal(5, pieces.Count);
        Near(6 * D, pieces[0].End - pieces[0].Start);
        Assert.True(pieces[1].IsArc);
        Near(3 * Math.PI / 4, pieces[1].End - pieces[1].Start);
        // the arc touches the outer end of the edge: its centre lies radius + half a diameter from it
        Near(-33, pieces[1].Origin[0]);
        Near(-33, Start(pieces[2])[0]);
        Near(-(4921.35 - 33), End(pieces[2])[0]);
        Near(6 * D, pieces[4].End - pieces[4].Start);
    }

    [Fact]
    public void NinetyDegreeHookWithATangentLength_EndsThatFarFromTheMeetingPointOfLegAndEdge()
    {
        var hook = new RebarShapeSolver.Hook(Math.PI / 2, 44, -1, 350, 12);
        var pieces = RebarShapeSolver.Solve([[-1, 0, 0]], [360], [0, 0], D, 44, hook, hook)!;
        // the leg lies half a diameter inside the outer end of the edge and reaches 350 from the edge line
        Near(-4, Start(pieces[0])[0]);
        Near(350, Start(pieces[0])[1]);
        Near(44, End(pieces[0])[1]);
        Near(-356, End(pieces[4])[0]);
        Near(-356, Start(pieces[4])[0]);
        Near(350, End(pieces[4])[1]);
    }

    [Fact]
    public void ClosedStirrupWhoseHooksCollide_MovesTheLastEdgeAndTheEndHookOneDiameterAlongTheNormal()
    {
        var hook = new RebarShapeSolver.Hook(3 * Math.PI / 4, 29, 1, null, 6);
        var pieces = RebarShapeSolver.Solve([[-1, 0, 0], [0, -1, 0], [1, 0, 0], [0, 1, 0]], [250, 847.65, 247.65, 850], [0, 1, 1, 1, 0], D, 29, hook, hook)!;
        Assert.Equal(11, pieces.Count);
        // first edge and first corner in the plane, the bottom edge leans from 0 to -8 over its length between the corner centres
        Near(0, Start(pieces[2])[2]);
        var bottom = pieces[6];
        Assert.False(bottom.IsArc);
        Assert.True(bottom.Start == 0 && bottom.Axis[2] < 0);
        Near(-D / (247.65 - D), bottom.Axis[2] / bottom.Axis[0], 1e-9);
        // the last edge, its hook and the arc in front of it lie in the plane z = -8
        Near(-D, Start(pieces[8])[2]);
        Near(-D, End(pieces[8])[2]);
        Near(-D, pieces[9].Origin[2]);
        Near(-D, End(pieces[10])[2]);
        Near(-(2.35 + 4), Start(pieces[8])[0]);
    }

    [Fact]
    public void ClosedStirrupThatTurnsRight_IsNotCoveredAndGivesNothing()
    {
        var hook = new RebarShapeSolver.Hook(3 * Math.PI / 4, 29, -1, null, 6);
        Assert.Null(RebarShapeSolver.Solve([[1, 0, 0], [0, -1, 0], [-1, 0, 0], [0, 1, 0]], [250, 847.65, 247.65, 850], [0, -1, -1, -1, 0], D, 29, hook, hook));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void ShapeWithoutPositiveLengthOrWithAnOutOfPlaneDirection_GivesNothing(double length)
    {
        Assert.Null(RebarShapeSolver.Solve([[-1, 0, 0]], [length], [0, 0], D, 29, null, null));
        Assert.Null(RebarShapeSolver.Solve([[-1, 0, 1]], [10], [0, 0], D, 29, null, null));
    }

    // The walk past a blob it cannot read.

    private const int Root = 1, Opaque = 2, Value = 3;

    private static SchemaField Field(string name, byte type, byte mods = 0) => new(name, type, mods, 0, null, null, null, null);

    private static SchemaDocument Schema() => new()
    {
        Classes =
        [
            new(Root, "Root", 0, 1, [Field("m_items", 14, 81)], []),
            new(Opaque, "Opaque", 0, 1, [Field("m_entity", 14, 1)], []),
            new(Value, "Value", 0, 1, [Field("m_v", 7)], []),
        ],
    };

    private static byte[] BlockWithBlob(byte[] blob)
    {
        var stream = new MemoryStream();
        var w = new BinaryWriter(stream, Encoding.Unicode);
        w.Write((ushort)Root);
        w.Write(3u);
        foreach (int tag in new[] { Opaque, Value, Value }) { w.Write(1u); w.Write((ushort)tag); }
        // Opaque: a pointer to a class the schema does not declare, and the blob that follows it
        w.Write(1u); w.Write((ushort)9999);
        w.Write(blob);
        w.Write(1.0); w.Write(2.0);
        return stream.ToArray();
    }

    private static bool BothValuesRead(WideBlockWalker.Walk walk, int first) =>
        walk.Captures.Count(c => c.QueueIndex >= first && c.Key == "Value.m_v") == 2 && walk.Captures.Where(c => c.QueueIndex >= first).All(c => c.Real is 1.0 or 2.0);

    [Fact]
    public void Run_SkipsABlobItCannotReadWhenTheObjectsBehindItAreValidAtOnePlace()
    {
        var block = BlockWithBlob([0xFF, 0x13, 0x07, 0x77, 0x01, 0x80, 0x42]);
        var walker = new WideBlockWalker(Schema(), (d, f) => d.Name + "." + f.Name, []);
        Assert.NotNull(walker.Run(block, 0, block.Length, Root).Error);
        var walk = walker.Run(block, 0, block.Length, Root, BothValuesRead);
        Assert.Null(walk.Error);
        Assert.Equal(1.0, walk.Captures.Single(c => c.QueueIndex == 1).Real);
        Assert.Equal(2.0, walk.Captures.Single(c => c.QueueIndex == 2).Real);
    }

    [Fact]
    public void Run_KeepsTheErrorWhenNoPlaceOrSeveralPlacesFit()
    {
        var block = BlockWithBlob([0xFF, 0x13, 0x07, 0x77]);
        var walker = new WideBlockWalker(Schema(), (d, f) => d.Name + "." + f.Name, []);
        Assert.NotNull(walker.Run(block, 0, block.Length, Root, (_, _) => false).Error);
        // accepting everything leaves several start positions: not unique, so not resumed
        Assert.NotNull(walker.Run(block, 0, block.Length, Root, (_, _) => true).Error);
    }
}
