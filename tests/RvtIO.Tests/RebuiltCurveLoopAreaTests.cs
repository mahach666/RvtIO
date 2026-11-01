using RvtIO.Ifc;
using RvtIO.Internal;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

public class RebuiltCurveLoopAreaTests
{
    private static GeoFace QuarterRing(double x = 0, double y = 0)
    {
        double[] At(double radius, double angle) => [x + radius * Math.Cos(angle), y + radius * Math.Sin(angle), 0];
        return new GeoFace
        {
            OuterVertices = [At(2, 0), At(3, 0), At(3, Math.PI / 6), At(3, Math.PI / 3),
                At(3, Math.PI / 2), At(2, Math.PI / 2), At(2, Math.PI / 3), At(2, Math.PI / 6)],
            MeshArcs = [new Arc([x, y, 0], [1, 0, 0], [0, 1, 0], 2, 0, Math.PI / 2),
                new Arc([x, y, 0], [1, 0, 0], [0, 1, 0], 3, 0, Math.PI / 2)],
        };
    }

    [Fact]
    public void RebuiltArcProfile_MatchesTheIndependentlyMeasuredSyntheticCurveLoop()
    {
        // Quarter-annulus, radii 2 and 3 ft: the rebuilt loop's quadrature differs from the exact sector area.
        var area = IfcExporter.RebuiltHorizontalCurveLoopAreaCurrent(QuarterRing());
        Assert.NotNull(area);
        Assert.Equal(3.9270046025667518, area.Value, 11);
        Assert.NotEqual(5 * Math.PI / 4, area.Value, 9);
    }

    [Fact]
    public void RebuiltArcProfile_UsesItsOwnFrameRegardlessOfTranslationOrLoopDirection()
    {
        var moved = QuarterRing(50, -30);
        var area = IfcExporter.RebuiltHorizontalCurveLoopAreaCurrent(moved);
        Assert.Equal(IfcExporter.RebuiltHorizontalCurveLoopAreaCurrent(QuarterRing())!.Value, area!.Value, 10);
        moved.OuterVertices!.Reverse();
        Assert.Equal(area.Value, IfcExporter.RebuiltHorizontalCurveLoopAreaCurrent(moved)!.Value, 10);
    }

    [Fact]
    public void RebuiltArcProfile_RejectsIncompleteCurveSourceAndUnsupportedFaces()
    {
        var face = QuarterRing();
        face.MeshArcs = [.. face.MeshArcs!, new Arc([20, 0, 0], [1, 0, 0], [0, 1, 0], 1, 0, 1)];
        Assert.Null(IfcExporter.RebuiltHorizontalCurveLoopAreaCurrent(face));
        face = QuarterRing();
        face.InnerVertices = [[[0, 0, 0], [1, 0, 0], [0, 1, 0]]];
        Assert.Null(IfcExporter.RebuiltHorizontalCurveLoopAreaCurrent(face));
        face = QuarterRing();
        face.CurvedSurface = true;
        Assert.Null(IfcExporter.RebuiltHorizontalCurveLoopAreaCurrent(face));
    }
}
