using System.Globalization;
using System.Text.RegularExpressions;
using RvtIO.Ifc;
using RvtIO.Model;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The extruded solids the current profile writes for bodies other than walls and openings: a floor or a family type whose solids are all extrusions is a SweptSolid.</summary>
public class IfcExporterOthersExtrusionCurrentTests
{
    private const int FloorCategoryId = -2000032;
    private const double FeetToMillimetres = 304.8;

    [Fact]
    public void Box_ExtrudesAlongZFromItsLowerFace()
    {
        var extrusion = IfcExporter.CalculateOthersCurrentExtrusion(Box(4, 3, 0.3).Faces, IfcExporter.OthersCurrentAxesZ);

        Assert.NotNull(extrusion);
        Assert.Equal([0, 0, 1], extrusion.Axis);
        Assert.Equal(0.3, extrusion.LengthFt, 12);
        var region = Assert.Single(extrusion.Regions);
        Assert.All(region.Outer, point => Assert.Equal(0, point[2]));
        Assert.Equal(4, region.Outer.Count);
    }

    [Fact]
    public void Box_AllThreeAxesTryXFirst()
    {
        var extrusion = IfcExporter.CalculateOthersCurrentExtrusion(Box(4, 3, 0.3).Faces, IfcExporter.OthersCurrentAxesXyz);

        Assert.NotNull(extrusion);
        Assert.Equal([1, 0, 0], extrusion.Axis);
        Assert.Equal(4, extrusion.LengthFt, 12);
    }

    [Theory]
    [InlineData(0.0025, false)]
    [InlineData(0.0026, true)]
    public void ALoopWithACurveShorterThanShortCurveTol_LeavesNoExtrusion(double thickness, bool extruded)
    {
        // The lower face is thickness x 3 feet: its short edges are dropped by the kernel when it builds the loop (0.00256 ft).
        var extrusion = IfcExporter.CalculateOthersCurrentExtrusion(Box(thickness, 3, 2).Faces, IfcExporter.OthersCurrentAxesZ);

        Assert.Equal(extruded, extrusion != null);
    }

    [Fact]
    public void AVertexRepeatedWithinRoundingError_IsNoShortCurve()
    {
        var box = Box(4, 3, 2);
        var lower = box.Faces[0].OuterVertices!;
        lower.Insert(2, [lower[1][0] + 1e-14, lower[1][1], lower[1][2]]);

        Assert.NotNull(IfcExporter.CalculateOthersCurrentExtrusion(box.Faces, IfcExporter.OthersCurrentAxesZ));
    }

    [Theory]
    [InlineData(5e-11, true)]
    [InlineData(5e-10, false)]
    public void ASideFaceOffThePerpendicularByMoreThanTheGeTolerance_RejectsTheAxis(double tilt, bool extruded)
    {
        var box = Box(4, 3, 2);
        var side = box.Faces[2].OuterVertices!;
        side[2] = [side[2][0], side[2][1] - 2 * tilt, side[2][2]];
        side[3] = [side[3][0], side[3][1] - 2 * tilt, side[3][2]];

        Assert.Equal(extruded, IfcExporter.CalculateOthersCurrentExtrusion(box.Faces, IfcExporter.OthersCurrentAxesZ) != null);
    }

    [Fact]
    public void SlopedTop_HasNoExtrusion()
    {
        // (Along x the trapezoid prism is still an extrusion.)
        Assert.Null(IfcExporter.CalculateOthersCurrentExtrusion(Box(4, 3, 2, slopeTop: 1).Faces, IfcExporter.OthersCurrentAxesZ));
    }

    [Fact]
    public void AFaceWithAHoleOnTheSideRejectsTheAxis()
    {
        var box = Box(4, 3, 2);
        var side = box.Faces[2];
        side.InnerVertices = [[[1, 0, 0.5], [1, 0, 1], [2, 0, 1], [2, 0, 0.5]]];

        Assert.Null(IfcExporter.CalculateOthersCurrentExtrusion(box.Faces, IfcExporter.OthersCurrentAxesZ));
    }

    [Fact]
    public void ACapWhoseLoopCrossesItself_LeavesNoExtrusion()
    {
        double[][] hook = [[1, 0], [0, 0], [0, 2], [8, 2], [8, 10], [1, 10]];
        double[][] lShape = [[0, 0], [4, 0], [4, 2], [2, 2], [2, 4], [0, 4]];

        Assert.Null(IfcExporter.CalculateOthersCurrentExtrusion(Prism(hook, 1).Faces, IfcExporter.OthersCurrentAxesZ));
        Assert.NotNull(IfcExporter.CalculateOthersCurrentExtrusion(Prism(lShape, 1).Faces, IfcExporter.OthersCurrentAxesZ));
    }

    [Fact]
    public void ARingThatTouchesAnotherSegment_IntersectsItself_ButNotWhereOnlyEndPointsMeet()
    {
        static List<double[]> Ring(params double[][] points) => points.Select(p => new[] { p[0], p[1], 0 }).ToList();

        Assert.False(IfcExporter.OthersCurrentRingIntersectsItself(Ring([0, 0], [4, 0], [4, 3], [0, 3])));
        // A vertex of the ring on the interior of a segment that is not its neighbour.
        Assert.True(IfcExporter.OthersCurrentRingIntersectsItself(Ring([0, 0], [4, 0], [4, 3], [2, 3], [2, 0], [2, -1], [0, -1])));
        // The second segment runs back along the first and the third starts in the middle of the first.
        Assert.True(IfcExporter.OthersCurrentRingIntersectsItself(Ring([0, 0], [4, 0], [2, 0], [2, 3])));
        // Collinear neighbours that continue in the same direction are an ordinary chain of edges.
        Assert.False(IfcExporter.OthersCurrentRingIntersectsItself(Ring([0, 0], [2, 0], [4, 0], [4, 3], [0, 3])));
        // A repeated closing vertex is one vertex.
        Assert.False(IfcExporter.OthersCurrentRingIntersectsItself(Ring([0, 0], [4, 0], [4, 3], [0, 3], [0, 0])));
        Assert.False(IfcExporter.OthersCurrentRingIntersectsItself(Ring([0, 0], [2, 0], [2, 2], [4, 2], [4, 4], [2, 4], [2, 2], [0, 2])));
        // Within the Ge tolerance of 1e-10 a vertex touches the segment, beyond it it does not.
        Assert.True(IfcExporter.OthersCurrentRingIntersectsItself(Ring([0, 0], [4, 0], [4, 3], [2, 3], [2, 9e-11], [1, 1], [0, 1])));
        Assert.False(IfcExporter.OthersCurrentRingIntersectsItself(Ring([0, 0], [4, 0], [4, 3], [2, 3], [2, 1.1e-10], [1, 1], [0, 1])));
    }

    /// <summary>Polygons of an integer grid (x, y in 0..4, 3 to 8 vertices, repeated vertices allowed) with the expected outcome of appending the curves to a loop (1: every curve is appended and the loop is closed, 0: a curve is refused); a stratified selection of random polygons.</summary>
    public static IEnumerable<object[]> MeshCurveLoopAppendOutcomes() => new[]
    {
        "1|2,1;3,4;4,3;3,4",
        "1|2,0;2,3;3,3;4,3;4,0;3,3",
        "1|1,2;0,3;4,0;4,3;4,0;0,3",
        "1|3,3;4,2;2,4",
        "1|1,4;4,2;2,1;0,4;4,2",
        "1|2,4;1,2;2,4;0,0;1,0;2,1;1,1;2,2",
        "1|1,0;2,2;4,3;2,2;1,2",
        "1|3,1;3,2;4,4;1,3;3,4;4,4;1,3",
        "1|2,2;4,1;2,1;0,2;0,4;0,2",
        "1|2,1;0,0;1,2;2,1;2,2",
        "1|0,3;0,1;4,0;0,1",
        "1|1,0;3,1;1,0;1,2",
        "1|0,4;1,3;4,0",
        "1|0,1;4,0;3,1;2,4;4,0",
        "1|2,1;1,0;4,3",
        "1|1,2;2,1;1,2;1,3;0,0;2,1",
        "1|0,3;1,4;3,4",
        "1|1,2;0,4;1,1",
        "1|4,2;1,3;2,1",
        "1|0,3;1,3;2,1;4,4;2,3;2,4",
        "1|3,3;2,0;0,4",
        "1|2,1;3,4;4,1;0,0;0,3",
        "0|0,1;3,0;0,2;4,0",
        "0|0,4;0,0;2,4;4,2;2,2;1,3",
        "0|2,1;2,2;3,1;0,0;2,3;4,0;3,1;3,3",
        "0|0,2;4,4;3,2;0,3;2,1",
        "0|4,1;3,2;0,1;0,4;2,2",
        "0|4,3;0,1;2,2;2,1;0,1;1,0;2,0;2,4",
        "0|0,2;4,3;4,2;2,0;3,1;0,4;1,2;3,0",
        "0|4,3;1,1;0,4;3,3;3,1;3,4",
        "0|4,2;3,3;4,3;0,3;2,1;0,1",
        "0|1,0;1,1;4,1;2,1;4,1;3,1;4,4;4,3",
        "0|3,2;4,4;0,2;1,3;0,2;0,3;4,2;1,4",
        "0|2,0;1,0;0,4;1,3;0,4;4,2;2,2;0,1",
        "0|3,3;4,1;1,3;0,0;1,3;2,0;4,0",
        "0|3,1;0,1;0,3;2,0;0,3;4,3",
        "0|1,3;3,3;1,3;0,4;1,4;3,1;4,4",
        "0|0,0;3,1;0,1;3,2;3,4;0,3",
        "0|1,2;4,1;3,0;0,3;2,2",
        "0|1,2;2,4;4,0;1,0;3,2;1,1",
        "0|3,3;1,2;0,2;2,3;4,3;4,2",
        "0|1,3;3,1;2,4;3,2;3,0",
        "0|2,3;1,2;2,0;4,3;0,3",
        "0|3,2;1,0;2,3;2,1",
        "0|2,1;4,1;3,1;2,3;1,0",
        "0|1,1;3,3;1,1;0,4;1,1;2,4;2,2",
        "0|3,0;1,4;1,0;0,0",
        "0|3,4;0,3;2,1;1,0;2,3;1,2;1,4",
        "0|1,4;0,2;3,2;4,2;0,0;1,4;3,4;3,1",
        "0|2,1;1,4;3,0;4,1;1,1;3,4",
        "0|0,4;4,0;0,1;4,4;3,1;3,3;0,0",
        "0|1,1;2,4;4,3;1,1;0,3;0,4;4,4",
        "0|0,1;0,2;0,0;0,4",
        "0|0,1;4,0;0,4;0,2;0,4;0,3",
        "0|0,4;0,2;0,0;0,1",
        "0|4,3;2,3;3,3;4,3;1,3;1,0",
        "0|1,4;4,2;4,4;2,4;4,4;3,4",
    }.Select(line => new object[] { line });

    [Theory]
    [MemberData(nameof(MeshCurveLoopAppendOutcomes))]
    public void RingIntersectsItself_AgreesWithTheCurveLoopAppendOfTheReference(string line)
    {
        var parts = line.Split('|');
        var ring = parts[1].Split(';').Select(point => point.Split(',').Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).Append(0).ToArray()).ToList();

        Assert.Equal(parts[0] == "0", IfcExporter.OthersCurrentRingIntersectsItself(ring));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1e-11, false)]
    [InlineData(1e-9, true)]
    public void ABoxCentredOnTheOriginOfItsFamily_IsABrepAlongZ(double shift, bool extruded)
    {
        var centred = Prism([[-2 + shift, -1.5], [2 + shift, -1.5], [2 + shift, 1.5], [-2 + shift, 1.5]], 1);

        Assert.Equal(extruded, IfcExporter.CalculateOthersCurrentExtrusion(centred.Faces, IfcExporter.OthersCurrentAxesZ) != null);
    }

    [Fact]
    public void ABoxCentredOnTheOriginIsStillAnExtrusionAlongX_AndAboveTheOriginAlongZ()
    {
        var centred = Prism([[-2, -1.5], [2, -1.5], [2, 1.5], [-2, 1.5]], 1);
        var extrusion = IfcExporter.CalculateOthersCurrentExtrusion(centred.Faces, IfcExporter.OthersCurrentAxesXyz);
        Assert.NotNull(extrusion);
        Assert.Equal([1, 0, 0], extrusion.Axis);

        var raised = Prism([[-2, -1.5], [2, -1.5], [2, 1.5], [-2, 1.5]], 1);
        foreach (var face in raised.Faces)
            foreach (var ring in new[] { face.OuterVertices }.Concat(face.InnerVertices ?? []))
                foreach (var point in ring!) point[2] += 0.5;
        Assert.NotNull(IfcExporter.CalculateOthersCurrentExtrusion(raised.Faces, IfcExporter.OthersCurrentAxesZ));
    }

    [Theory]
    [InlineData("0,0;4,0;4,3;0,3", false)]
    [InlineData("0,0;4,3;4,0;0,3", true)]
    [InlineData("0,0;4,0;4,3;2,3;2,0;2,-1;0,-1", true)]
    [InlineData("0,0;4,0;2,0;2,3", true)]
    [InlineData("2,0;4,0;0,0;0,3;2,3", true)]
    [InlineData("0,0;2,0;4,0;4,3;0,3", false)]
    [InlineData("0,0;2,2;4,0;4,4;2,2;0,4", false)]
    [InlineData("0,0;2,0;2,2;4,2;4,4;2,4;2,2;0,2", false)]
    [InlineData("0,0;4,0;4,4;0,4;0,2;2,2;0,2", false)]
    [InlineData("0,0;4,0;4,3;2.5,3;2,9e-11;1.5,3;0,3", true)]
    [InlineData("0,0;4,0;4,3;2.5,3;2,1e-10;1.5,3;0,3", false)]
    [InlineData("0,0;4,0;4,3;2.5,3;2,1.1e-10;1.5,3;0,3", false)]
    [InlineData("0,0;4,0;4,1;6,1;6,0;2,0;2,-2;0,-2", true)]
    [InlineData("1,0;0,0;0,2;8,2;8,10;1,10", true)]
    [InlineData("4,1;1,1;3,1", false)]
    [InlineData("1,3.5;1,1;3,1;1,1;3,1", false)]
    [InlineData("0,3;1,1;4,1;0,4;1,1", false)]
    [InlineData("3,1;3,0;1,0;4,2;2,1;4,2", false)]
    [InlineData("0,3;3,4;2,4;4,3;3,4;1,1", true)]
    [InlineData("1,4;2,0;4,0;1,1;2,0;3,3", true)]
    public void RingIntersection_FollowsTheCurveLoopOfTheKernel(string ring, bool intersects)
    {
        var points = ring.Split(';').Select(p => p.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).Append(0).ToArray()).ToList();

        Assert.Equal(intersects, IfcExporter.OthersCurrentRingIntersectsItself(points));
        // a repeated closing vertex is one vertex
        Assert.Equal(intersects, IfcExporter.OthersCurrentRingIntersectsItself([.. points, points[0]]));
    }

    [Fact]
    public void FlowSegment_KeepsItsFacetedBody()
    {
        var segment = Floor(Box(4, 3, 0.3));
        segment.CategoryId = -2008044;
        segment.Category = "OST_PipeCurves";
        string ifc = Export(segment, IfcProfile.Current);
        Assert.Contains("=IFCFLOWSEGMENT(", ifc);
        Assert.Contains("=IFCFACETEDBREP(", ifc);
        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", ifc);
    }

    [Fact]
    public void MappedFlowController_KeepsItsFacetedBody()
    {
        string ifc = ExportMapped(Box(0.1, 3, 2), "OST_PipeAccessory", -2008055, IfcProfile.Current);
        Assert.Contains("=IFCFLOWCONTROLLER(", ifc);
        Assert.Contains("=IFCFACETEDBREP(", ifc);
        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", ifc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CircularProfile_UsesTheSerializedCylinderCentreAndRadius(bool matchingCentre)
    {
        double[] degrees = [0, 10, 40, 80, 120, 160, 210, 270, 320];
        var geometry = Prism(degrees.Select(angle => new[]
        {
            5 + 2 * Math.Cos(angle * Math.PI / 180), 5 + 2 * Math.Sin(angle * Math.PI / 180),
        }).ToArray(), 3);
        foreach (var face in geometry.Faces.Skip(2))
        {
            face.CurvedSurface = true;
            face.AnalyticCylinderOrigin = matchingCentre ? [5, 5, 0] : [0, 0, 0];
            face.AnalyticCylinderAxis = [0, 0, 1];
            face.AnalyticCylinderRadius = 2;
        }

        string ifc = Export(Floor(geometry), IfcProfile.Current);
        var profiles = Regex.Matches(ifc, @"=IFCCIRCLEPROFILEDEF\(\.AREA\.,'[^']*',#\d+,([^)]*)\);");
        if (!matchingCentre)
        {
            Assert.Empty(profiles);
            return;
        }
        var profile = Assert.Single(profiles);
        Assert.Equal(2 * FeetToMillimetres, double.Parse(profile.Groups[1].Value, CultureInfo.InvariantCulture), 9);
        var origin = SolidOrigin(ifc);
        Assert.Equal(5 * FeetToMillimetres, origin.X, 9);
        Assert.Equal(5 * FeetToMillimetres, origin.Y, 9);
    }

    private static GeometryData QuarterRoundPrism(Action<GeoFace> markCurvedFace)
    {
        double[] degrees = [0, 15, 30, 45, 60, 75, 90];
        double[][] outline = [[0, 0], .. degrees.Select(angle => new[]
        {
            2 * Math.Cos(angle * Math.PI / 180), 2 * Math.Sin(angle * Math.PI / 180),
        })];
        var geometry = Prism(outline, 3);
        foreach (var face in geometry.Faces.Skip(3).Take(degrees.Length - 1))
        {
            face.CurvedSurface = true;
            markCurvedFace(face);
        }
        return geometry;
    }

    [Fact]
    public void PartialCoaxialCylinder_IsExtrudedOverItsTessellatedBoundary()
    {
        var geometry = QuarterRoundPrism(face =>
        {
            face.AnalyticCylinderOrigin = [0, 0, 0];
            face.AnalyticCylinderAxis = [0, 0, 1];
            face.AnalyticCylinderRadius = 2;
        });

        string ifc = Export(Floor(geometry), IfcProfile.Current);

        Assert.Contains("=IFCEXTRUDEDAREASOLID(", ifc);
        Assert.Contains("=IFCARBITRARYCLOSEDPROFILEDEF(", ifc);
        Assert.DoesNotContain("=IFCFACETEDBREP(", ifc);
    }

    /// <summary>The quarter-round prism with the authored Edge samples: each face's own vertex box, with the samples stopping <paramref name="shortBy"/> feet short of the apex of the arc at x = 2.</summary>
    private static GeometryData QuarterRoundPrismWithAuthoredSamples(double shortBy)
    {
        var geometry = QuarterRoundPrism(face =>
        {
            face.AnalyticCylinderOrigin = [0, 0, 0];
            face.AnalyticCylinderAxis = [0, 0, 1];
            face.AnalyticCylinderRadius = 2;
        });
        uint source = 1;
        foreach (var face in geometry.Faces)
        {
            var points = face.OuterVertices!;
            face.SourceFace = source++;
            face.MeshEdgeBounds =
            [
                points.Min(p => p[0]), points.Min(p => p[1]), points.Min(p => p[2]),
                Math.Min(points.Max(p => p[0]), 2 - shortBy), points.Max(p => p[1]), points.Max(p => p[2]),
            ];
        }
        return geometry;
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.01, false)]
    public void CurvedSolid_IsJudgedByTheBoxOfItsAuthoredEdgeSamples(double shortBy, bool extruded)
    {
        var geometry = QuarterRoundPrismWithAuthoredSamples(shortBy);

        var extrusion = IfcExporter.CalculateOthersCurrentExtrusion(geometry.Faces, IfcExporter.OthersCurrentAxesZ);

        Assert.Equal(extruded, extrusion != null);
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.01, false)]
    public void ABeamWithACurvedProfile_IsJudgedByTheBoxOfItsAuthoredEdgeSamples_OnTheAxesOfItsDriver(double shortBy, bool extruded)
    {
        var geometry = QuarterRoundPrismWithAuthoredSamples(shortBy);
        var axes = new IfcExporter().OthersCurrentSymbolAxes(new Element { CurveDriverLineDirection = (0, 0, 1) }, "IFCBEAM")!;

        var extrusion = IfcExporter.CalculateOthersCurrentExtrusion(geometry.Faces, axes);

        Assert.Equal(extruded, extrusion != null);
        if (extruded) Assert.Equal([0, 0, 1], extrusion!.Axis);
    }

    [Fact]
    public void CurvedSolid_TakesTheStoredEnvelopeOfTheEndFaces()
    {
        // The end faces span the short box through the envelope stored with the plane, whatever their dense outline reaches.
        var geometry = QuarterRoundPrismWithAuthoredSamples(0.01);
        foreach (var face in geometry.Faces.Take(2))
        {
            double z = face.OuterVertices![0][2];
            face.MeshPlaneEnvelopeCorners = [0, 0, z, 1.99, 2, z];
        }

        var extrusion = IfcExporter.CalculateOthersCurrentExtrusion(geometry.Faces, IfcExporter.OthersCurrentAxesZ);

        Assert.NotNull(extrusion);
    }

    [Fact]
    public void PartialCylinderAcrossTheAxis_KeepsItsFacetedBoundary()
    {
        // A cylinder whose axis is not the axis of the extrusion is no rounded edge of the profile.
        var geometry = QuarterRoundPrism(face =>
        {
            face.AnalyticCylinderOrigin = [0, 0, 0];
            face.AnalyticCylinderAxis = [1, 0, 0];
            face.AnalyticCylinderRadius = 2;
        });

        string ifc = Export(Floor(geometry), IfcProfile.Current);

        Assert.Contains("=IFCFACETEDBREP(", ifc);
        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", ifc);
    }

    [Fact]
    public void CurvedFaceWithoutACylinderAxis_KeepsItsFacetedBoundary()
    {
        string ifc = Export(Floor(QuarterRoundPrism(_ => { })), IfcProfile.Current);

        Assert.Contains("=IFCFACETEDBREP(", ifc);
        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", ifc);
    }

    [Fact]
    public void ReusedExporter_RecomputesTheExtrusionAfterGeometryChanges()
    {
        var geometry = Box(4, 3, 0.3);
        var info = new RevitFileInfo();
        info.Elements.Add(Floor(geometry));
        var exporter = new IfcExporter { Profile = IfcProfile.Current };
        using var first = new StringWriter();
        exporter.Export(info, first);
        foreach (var face in geometry.Faces)
            foreach (var point in face.OuterVertices!)
                point[2] *= 2;
        geometry.BoundingBox![5] = 0.6;

        using var second = new StringWriter();
        exporter.Export(info, second);
        var solid = Assert.Single(Regex.Matches(second.ToString(), @"=IFCEXTRUDEDAREASOLID\(#\d+,#\d+,#\d+,([^)]*)\);"));
        Assert.Equal(0.6 * FeetToMillimetres, double.Parse(solid.Groups[1].Value, CultureInfo.InvariantCulture), 9);
    }

    [Fact]
    public void FloorCurrent_IsAnExtrudedRectangleInMillimetres()
    {
        string ifc = Export(Floor(Box(4, 3, 0.3)), IfcProfile.Current);

        Assert.Contains("'Body','SweptSolid'", ifc);
        Assert.DoesNotContain("=IFCFACETEDBREP(", ifc);
        var solid = Assert.Single(Regex.Matches(ifc, @"=IFCEXTRUDEDAREASOLID\(#(\d+),#(\d+),#(\d+),([^)]*)\);"));
        // 0.3 ft thick.
        Assert.Equal(0.3 * FeetToMillimetres, double.Parse(solid.Groups[4].Value, CultureInfo.InvariantCulture), 6);
        var profile = Assert.Single(Regex.Matches(ifc, @"=IFCRECTANGLEPROFILEDEF\(\.AREA\.,'([^']*)',#\d+,([^,]*),([^)]*)\);"));
        Assert.Equal(3 * FeetToMillimetres, double.Parse(profile.Groups[2].Value, CultureInfo.InvariantCulture), 6);
        Assert.Equal(4 * FeetToMillimetres, double.Parse(profile.Groups[3].Value, CultureInfo.InvariantCulture), 6);
        // The type name is the profile name; a horizontal profile is placed with the default axis and reference direction.
        Assert.Equal("Floor type", profile.Groups[1].Value);
        Assert.Matches(@"=IFCAXIS2PLACEMENT3D\(#\d+,\$,\$\);", ifc);
    }

    [Fact]
    public void FloorLegacy_StaysFaceted()
    {
        string ifc = Export(Floor(Box(4, 3, 0.3)), IfcProfile.Legacy);

        Assert.Contains("=IFCFACETEDBREP(", ifc);
        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", ifc);
    }

    [Fact]
    public void LShapedFloor_IsAnArbitraryProfileCounterClockwiseFromTheStartOfTheLoop()
    {
        double[][] outline = [[0, 0], [4, 0], [4, 1], [1, 1], [1, 3], [0, 3]];
        string ifc = Export(Floor(Prism(outline, 0.5)), IfcProfile.Current);

        var profile = Assert.Single(Regex.Matches(ifc, @"=IFCARBITRARYCLOSEDPROFILEDEF\(\.AREA\.,'Floor type',#(\d+)\);"));
        var points = PolylinePoints(ifc, int.Parse(profile.Groups[1].Value, CultureInfo.InvariantCulture));

        Assert.Equal(outline.Length + 1, points.Count);
        Assert.Equal(points[0], points[^1]);
        double meanX = outline.Average(p => p[0]), meanY = outline.Average(p => p[1]);
        Assert.Equal((0 - meanX) * FeetToMillimetres, points[0].X, 6);
        Assert.Equal((0 - meanY) * FeetToMillimetres, points[0].Y, 6);
        Assert.Equal((4 - meanX) * FeetToMillimetres, points[1].X, 6);
        Assert.Equal((0 - meanY) * FeetToMillimetres, points[1].Y, 6);
        Assert.True(SignedArea(points.Take(outline.Length).ToList()) > 0);
        var origin = SolidOrigin(ifc);
        Assert.Equal(meanX * FeetToMillimetres, origin.X, 6);
        Assert.Equal(meanY * FeetToMillimetres, origin.Y, 6);
        Assert.Equal(0, origin.Z, 6);
    }

    [Fact]
    public void FloorWithAHole_IsAProfileWithVoids_ClockwiseHole()
    {
        var floor = Box(4, 3, 0.3);
        double[][] hole = [[1, 1], [2, 1], [2, 2], [1, 2]];
        foreach (int index in new[] { 0, 1 })   // the lower and the upper face
        {
            double z = index == 0 ? 0 : 0.3;
            floor.Faces[index].InnerVertices = [hole.Select(p => new[] { p[0], p[1], z }).ToList()];
        }
        string ifc = Export(Floor(floor), IfcProfile.Current);

        var profile = Assert.Single(Regex.Matches(
            ifc, @"=IFCARBITRARYPROFILEDEFWITHVOIDS\(\.AREA\.,'Floor type',#(\d+),\(#(\d+)\)\);"));
        var outer = PolylinePoints(ifc, int.Parse(profile.Groups[1].Value, CultureInfo.InvariantCulture));
        var inner = PolylinePoints(ifc, int.Parse(profile.Groups[2].Value, CultureInfo.InvariantCulture));
        Assert.True(SignedArea(outer.Take(outer.Count - 1).ToList()) > 0);
        Assert.True(SignedArea(inner.Take(inner.Count - 1).ToList()) < 0);
    }

    [Fact]
    public void FloorWithTwoSeparateShells_IsACompositeProfile()
    {
        var first = Box(4, 3, 0.3);
        var second = Box(2, 2, 0.3, offsetX: 10);
        var faces = first.Faces.Concat(second.Faces).ToList();
        var geometry = new GeometryData
        {
            BoundingBox = [0, 0, 0, 12, 3, 0.3],
            Faces = faces,
            BrepSolids =
            [
                new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] },
                new GeoBrepSolid { IsClosed = true, FaceIndices = [6, 7, 8, 9, 10, 11] },
            ],
        };
        // One Geometry node holding both shells: the extrusion data merges the two equal-length pairs of faces.
        foreach (var face in faces) face.GeometryNode = 1;
        string ifc = Export(Floor(geometry), IfcProfile.Current);

        var composite = Assert.Single(Regex.Matches(ifc, @"=IFCCOMPOSITEPROFILEDEF\(\.AREA\.,'Floor type',\(([^)]*)\),\$\);"));
        Assert.Equal(2, composite.Groups[1].Value.Split(',').Length);
        Assert.Single(Regex.Matches(ifc, @"=IFCEXTRUDEDAREASOLID\("));
    }

    [Fact]
    public void FloorWithASlopedSolid_IsFacetedWhole()
    {
        var flat = Box(4, 3, 0.3);
        var sloped = Box(2, 2, 0.3, offsetX: 10, slopeTop: 0.2);
        var faces = flat.Faces.Concat(sloped.Faces).ToList();
        for (int i = 0; i < faces.Count; i++) faces[i].GeometryNode = i < 6 ? 1 : 2;
        var geometry = new GeometryData
        {
            BoundingBox = [0, 0, 0, 12, 3, 0.5],
            Faces = faces,
            BrepSolids =
            [
                new GeoBrepSolid { IsClosed = true, FaceIndices = [0, 1, 2, 3, 4, 5] },
                new GeoBrepSolid { IsClosed = true, FaceIndices = [6, 7, 8, 9, 10, 11] },
            ],
        };
        string ifc = Export(Floor(geometry), IfcProfile.Current);

        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", ifc);
        Assert.Equal(2, Regex.Matches(ifc, @"=IFCFACETEDBREP\(").Count);
    }

    [Fact]
    public void FloorWithAnOpenShell_StaysFaceted()
    {
        var geometry = Box(4, 3, 0.3);
        geometry.BrepSolids![0].IsClosed = false;
        string ifc = Export(Floor(geometry), IfcProfile.Current);

        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", ifc);
    }

    [Fact]
    public void MappedPlate_ExtrudesAlongXInTheFrameOfTheLoop()
    {
        // A panel 0.1 ft thick in x, 3 ft wide in y and 2 ft high: the x axis finds the pair of big faces first.
        string ifc = ExportMapped(Box(0.1, 3, 2), "OST_GenericModel", -2000151, IfcProfile.Current);

        Assert.Single(Regex.Matches(ifc, @"=IFCREPRESENTATIONMAP\("));
        Assert.Contains("'Body','SweptSolid'", ifc);
        Assert.DoesNotContain("=IFCFACETEDBREP(", ifc);
        // Normal +x, plane x along +y and plane y up: Axis (1,0,0) and RefDirection (0,1,0).
        Assert.Matches(@"=IFCAXIS2PLACEMENT3D\(#\d+,#(\d+),#(\d+)\);", ifc);
        var axis = Regex.Match(ifc, @"=IFCAXIS2PLACEMENT3D\(#\d+,#(\d+),#(\d+)\);");
        Assert.Equal("(1.,0.,0.)", DirectionOf(ifc, axis.Groups[1].Value));
        Assert.Equal("(0.,1.,0.)", DirectionOf(ifc, axis.Groups[2].Value));
        // The face x = 0 is listed (3,0), (0,0), (0,2), (3,2) in (y, z): clockwise about +x, so it is turned keeping the start,
        // v0..v3 = (3,0), (3,2), (0,2), (0,0). The x side runs v3 to v0, 3 ft along y, and the y side v3 to v2, 2 ft up.
        var profile = Assert.Single(Regex.Matches(ifc, @"=IFCRECTANGLEPROFILEDEF\(\.AREA\.,'[^']*',#\d+,([^,]*),([^)]*)\);"));
        Assert.Equal(3 * FeetToMillimetres, double.Parse(profile.Groups[1].Value, CultureInfo.InvariantCulture), 6);
        Assert.Equal(2 * FeetToMillimetres, double.Parse(profile.Groups[2].Value, CultureInfo.InvariantCulture), 6);
    }

    [Fact]
    public void MappedColumn_TriesOnlyTheZAxis()
    {
        // The same box as a column symbol: z only (an extrusion-friendly type without a structural axis).
        string ifc = ExportMapped(Box(0.1, 3, 2), "OST_StructuralColumns", -2001330, IfcProfile.Current);

        Assert.Contains("=IFCEXTRUDEDAREASOLID(", ifc);
        Assert.Matches(@"=IFCAXIS2PLACEMENT3D\(#\d+,\$,\$\);", ifc);
        var profile = Assert.Single(Regex.Matches(ifc, @"=IFCRECTANGLEPROFILEDEF\(\.AREA\.,'[^']*',#\d+,([^,]*),([^)]*)\);"));
        Assert.Equal(3 * FeetToMillimetres, double.Parse(profile.Groups[1].Value, CultureInfo.InvariantCulture), 6);
        Assert.Equal(0.1 * FeetToMillimetres, double.Parse(profile.Groups[2].Value, CultureInfo.InvariantCulture), 6);
    }

    [Fact]
    public void MappedLegacy_StaysFaceted()
    {
        string ifc = ExportMapped(Box(0.1, 3, 2), "OST_GenericModel", -2000151, IfcProfile.Legacy);

        Assert.Contains("=IFCFACETEDBREP(", ifc);
        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", ifc);
    }

    private static GeometryData Box(double x, double y, double z, double offsetX = 0, double slopeTop = 0) =>
        Prism([[0, 0], [x, 0], [x, y], [0, y]], z, offsetX, slopeTop);

    /// <summary>A prism over a counter-clockwise outline (feet): the lower face is listed clockwise from above, the upper counter-clockwise,
    /// the sides outward. <paramref name="slopeTop"/> lifts the upper vertices with y beyond the outline's minimum y.</summary>
    private static GeometryData Prism(double[][] outline, double height, double offsetX = 0, double slopeTop = 0)
    {
        int n = outline.Length;
        double minY = outline.Min(p => p[1]);
        double[] Lower(int i) => [outline[i][0] + offsetX, outline[i][1], 0];
        double[] Upper(int i) => [outline[i][0] + offsetX, outline[i][1], height + (outline[i][1] > minY ? slopeTop : 0)];
        var faces = new List<GeoFace>
        {
            new() { OuterVertices = Enumerable.Range(0, n).Select(Lower).ToList() },
            new() { OuterVertices = Enumerable.Range(0, n).Select(Upper).ToList() },
        };
        // The lower face is clockwise seen from above when the outline is counter-clockwise: reverse the listing but keep the first vertex.
        faces[0].OuterVertices = [faces[0].OuterVertices![0], .. faces[0].OuterVertices!.Skip(1).Reverse()];
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            faces.Add(new GeoFace { OuterVertices = [Lower(i), Lower(next), Upper(next), Upper(i)] });
        }
        return new GeometryData
        {
            BoundingBox = [offsetX, 0, 0, outline.Max(p => p[0]) + offsetX, outline.Max(p => p[1]), height + slopeTop],
            Faces = faces,
            BrepSolids = [new GeoBrepSolid { IsClosed = true, FaceIndices = Enumerable.Range(0, faces.Count).ToList() }],
        };
    }

    private static Element Floor(GeometryData geometry) => new()
    {
        Id = 900,
        TypeId = 901,
        IsDiscovered = true,
        CategoryId = FloorCategoryId,
        Category = "OST_Floors",
        Name = "Floor type",
        References = [3],
        Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 }],
        BoundingBox = geometry.BoundingBox,
        Geometry = geometry,
    };

    private static string Export(Element element, IfcProfile version)
    {
        var info = new RevitFileInfo();
        info.Elements.Add(element);
        return Export(info, version);
    }

    private static string Export(RevitFileInfo info, IfcProfile version)
    {
        string path = Path.Combine(Path.GetTempPath(), $"revitreader-others-extrusion-{Guid.NewGuid():N}.ifc");
        try
        {
            new IfcExporter { Profile = version }.Export(info, path);
            return File.ReadAllText(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(false, "IFCEXTRUDEDAREASOLID")]
    [InlineData(true, "IFCFACETEDBREP")]
    public void ColumnWithABodyOfItsOwn_IsExtrudedAlongZ_UnlessItIsNoPrism(bool sloped, string expected)
    {
        var element = Floor(Box(2, 1, 10, offsetX: 5, slopeTop: sloped ? 1 : 0));
        element.CategoryId = -2001330;
        element.Category = "OST_StructuralColumns";
        element.Name = "Column type";
        element.SerializedOwnElementClassId = 1794;

        string ifc = Export(element, IfcProfile.Current);

        Assert.Contains("=IFCCOLUMN(", ifc);
        Assert.Contains("=" + expected + "(", ifc);
        Assert.Equal(sloped, ifc.Contains("=IFCFACETEDBREP("));
        // The legacy profile has no such extrusion
        Assert.DoesNotContain("=IFCEXTRUDEDAREASOLID(", Export(element, IfcProfile.Legacy));
    }

    [Theory]
    [InlineData(false, "IFCFACEBASEDSURFACEMODEL")]
    [InlineData(true, "IFCFACETEDBREP")]
    public void SystemRailingWithAnOpenTopology_IsABrepOnlyWhenItsPolygonsCloseExactly(bool closesExactly, string expected)
    {
        var box = Box(0.1, 3, 2);
        box.BrepSolids![0].IsClosed = false;
        if (!closesExactly) { box.Faces.RemoveAt(5); box.BrepSolids[0].FaceIndices.RemoveAt(5); }
        var element = new Element
        {
            Id = 202, TypeId = 201, IsDiscovered = true, CategoryId = -2000126, Category = "OST_StairsRailing", Name = "Railing",
            SerializedOwnElementClassId = 485, References = [3], BoundingBox = box.BoundingBox, Geometry = box,
            Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 }],
        };

        string ifc = Export(element, IfcProfile.Current);

        Assert.Contains("=IFCRAILING(", ifc);
        Assert.Contains("=" + expected + "(", ifc);
    }

    [Theory]
    [InlineData((ushort)1794, true)]   // a railing that is a family instance: the type has a representation map
    [InlineData((ushort)485, false)]   // a system railing: the railing export writes plain items
    public void Railing_IsMappedOnlyWhenItIsAFamilyInstance(ushort ownClass, bool mapped)
    {
        string ifc = ExportMapped(Box(0.1, 3, 2), "OST_StairsRailing", -2000126, IfcProfile.Current, ownClass);

        Assert.Equal(mapped, ifc.Contains("=IFCMAPPEDITEM("));
        Assert.True(ifc.Contains("=IFCEXTRUDEDAREASOLID(") || ifc.Contains("=IFCFACETEDBREP("));
    }

    private static string ExportMapped(GeometryData symbolGeometry, string category, int categoryId, IfcProfile version,
        ushort? ownElementClassId = null)
    {
        const int symbolId = 201;
        var instanceGeometry = new GeometryData
        {
            BoundingBox = symbolGeometry.BoundingBox,
            Faces = symbolGeometry.Faces,
            BrepSolids = symbolGeometry.BrepSolids,
            ExactMappedInstances = [new GeoInstance { SymbolId = symbolId, Transform = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0] }],
        };
        var info = new RevitFileInfo();
        info.Elements.Add(new Element { Id = symbolId, IsBlockOnly = true, Geometry = symbolGeometry });
        info.Elements.Add(new Element
        {
            Id = 202,
            TypeId = symbolId,
            IsDiscovered = true,
            CategoryId = categoryId,
            Category = category,
            Name = "Mapped box",
            SerializedOwnElementClassId = ownElementClassId,
            References = [3],
            Parameters = [new Parameter { Id = -1012100, StorageType = StorageType.ElementId, Value = 3 }],
            BoundingBox = instanceGeometry.BoundingBox,
            Geometry = instanceGeometry,
        });
        return Export(info, version);
    }

    private static string DirectionOf(string ifc, string id)
    {
        var match = Regex.Match(ifc, $@"#{id}=IFCDIRECTION\((\([^)]*\))\);");
        Assert.True(match.Success, $"direction #{id}");
        return match.Groups[1].Value;
    }

    private static List<(double X, double Y)> PolylinePoints(string ifc, int curveId)
    {
        var curve = Regex.Match(ifc, $@"#{curveId}=IFCPOLYLINE\(\(([^)]*)\)\);");
        Assert.True(curve.Success, $"polyline #{curveId}");
        return curve.Groups[1].Value.Split(',').Select(reference =>
        {
            var point = Regex.Match(ifc, $@"{Regex.Escape(reference)}=IFCCARTESIANPOINT\(\(([^,]*),([^)]*)\)\);");
            Assert.True(point.Success, $"point {reference}");
            return (double.Parse(point.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(point.Groups[2].Value, CultureInfo.InvariantCulture));
        }).ToList();
    }

    private static double SignedArea(IReadOnlyList<(double X, double Y)> points)
    {
        double twice = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var (x1, y1) = points[i];
            var (x2, y2) = points[(i + 1) % points.Count];
            twice += x1 * y2 - x2 * y1;
        }
        return twice / 2;
    }

    /// <summary>The location of the position of the only extruded solid, millimetres.</summary>
    private static (double X, double Y, double Z) SolidOrigin(string ifc)
    {
        var solid = Assert.Single(Regex.Matches(ifc, @"=IFCEXTRUDEDAREASOLID\(#\d+,#(\d+),"));
        var placement = Regex.Match(ifc, $@"#{solid.Groups[1].Value}=IFCAXIS2PLACEMENT3D\(#(\d+),");
        Assert.True(placement.Success);
        var point = Regex.Match(ifc, $@"#{placement.Groups[1].Value}=IFCCARTESIANPOINT\(\(([^,]*),([^,]*),([^)]*)\)\);");
        Assert.True(point.Success);
        double Number(int group) => double.Parse(point.Groups[group].Value, CultureInfo.InvariantCulture);
        return (Number(1), Number(2), Number(3));
    }
}
