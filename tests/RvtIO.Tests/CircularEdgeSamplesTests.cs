using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

public class CircularEdgeSamplesTests
{
    [Theory]
    [InlineData(0.9, 1.37712235, 6)]
    [InlineData(1.13, 1.60343876, 8)]
    [InlineData(0.455, 1.32943204, 5)]
    [InlineData(0.455, -1.32943204, 5)]
    public void NativeLowDeviationReproducesCircularBoundaryCounts(double radiusMetres, double span, int count)
    {
        foreach (bool cylinderFirst in new[] { false, true })
        {
            var plane = Plane(); var cylinder = Cylinder(radiusMetres / 0.3048);
            var edge = Edge(span, cylinderFirst);
            GRepParserV2.ResampleCircularEdge(edge, cylinderFirst ? cylinder : plane, cylinderFirst ? plane : cylinder);
            var points = new[] { edge.Start }.Concat(edge.Interior).Append(edge.EndP).ToList();
            Assert.Equal(count, points.Count);
            for (int i = 0; i < count; i++)
            {
                var p = points[i]; double u = span * i / (count - 1);
                Assert.Equal(u, cylinderFirst ? p.UA : p.UB, 12);
                Assert.Equal(cylinder.R * Math.Cos(u), cylinderFirst ? p.UB : p.UA, 12);
                Assert.Equal(cylinder.R * Math.Sin(u), cylinderFirst ? p.VB : p.VA, 12);
            }
        }
    }

    [Theory]
    [InlineData("missing_endpoint")] [InlineData("endpoint_flag")] [InlineData("zero_radius")]
    [InlineData("nonfinite_radius")] [InlineData("nonfinite_angle")] [InlineData("ellipse")]
    [InlineData("noncircular_height")] [InlineData("nonmonotonic")] [InlineData("singular_plane")]
    [InlineData("nonfinite_height")] [InlineData("nonfinite_interior_height")]
    public void UnvalidatedCircularEdgesRetainTheirAuthoredSamples(string mutation)
    {
        var plane = Plane(); var cylinder = Cylinder(3); var edge = Edge(1, false);
        switch (mutation)
        {
            case "missing_endpoint": edge.HasEndpoints = false; break;
            case "endpoint_flag": edge.Flags = 2; break;
            case "zero_radius": cylinder.R = 0; break;
            case "nonfinite_radius": cylinder.R = double.NaN; break;
            case "nonfinite_angle": edge.EndP.UB = double.NaN; break;
            case "nonfinite_height": edge.EndP.VB = double.NaN; break;
            case "nonfinite_interior_height": edge.Interior.Add(new() { UB = 0.5, VB = double.NaN }); break;
            case "ellipse": plane.X[2] = 0.1; break;
            case "noncircular_height": edge.EndP.VB = 1; break;
            case "nonmonotonic": edge.Interior.Add(new() { UB = 2 }); break;
            case "singular_plane": plane.Y = plane.X; break;
        }
        var start = edge.Start; var end = edge.EndP; var interior = edge.Interior;
        GRepParserV2.ResampleCircularEdge(edge, plane, cylinder);
        Assert.Equal(start, edge.Start); Assert.Equal(end, edge.EndP); Assert.Same(interior, edge.Interior);
    }

    private static GRepParserV2.SurfaceRec Plane() => new() { Kind = GRepParserV2.SurfKind.Plane,
        X = [1, 0, 0], Y = [0, 1, 0] };
    private static GRepParserV2.SurfaceRec Cylinder(double radius) => new() { Kind = GRepParserV2.SurfKind.Cylinder,
        X = [1, 0, 0], Y = [0, 1, 0], Z = [0, 0, 1], R = radius };
    private static GRepParserV2.EdgeRec Edge(double span, bool cylinderFirst) => new() { HasEndpoints = true, Flags = 6,
        EndP = cylinderFirst ? new() { UA = span } : new() { UB = span } };
}
