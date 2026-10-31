using RvtIO.Geometry.Meshing;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The pure rules of the GRep decoder that feeds the face mesher: the flip bit, the circle of an
/// edge between a plane and a cylinder, the circle through the samples of an edge, the revolved surface and its edges.</summary>
public class FaceDecoderTests
{
    // GFace.m_faceFlags_v9 bit 1 against Surface.m_orientFlag: Test_2022.rvt, 8689 of 8909 matched faces (the rest belong to 28 symbols of mirrored instances)
    [Theory]
    [InlineData(4u, (byte)1, false)]
    [InlineData(6u, (byte)1, true)]
    [InlineData(4u, (byte)0, true)]
    [InlineData(6u, (byte)0, false)]
    public void TheFlipBitIsTheFaceFlagBitAgainstTheOrientFlag(uint faceFlags, byte orient, bool flipped) =>
        Assert.Equal(flipped, GRepParserV2.MeshFlippedBits(faceFlags, orient));

    private static GRepParserV2.SurfaceRec Cylinder() => new()
    {
        Kind = GRepParserV2.SurfKind.Cylinder, O = [1, 2, 3], X = [1, 0, 0], Y = [0, 1, 0], Z = [0, 0, 1], R = 2,
        Env = [0, 0, 2 * Math.PI, 5],
    };

    [Fact]
    public void ACylinderArcWithAGrowingAngleKeepsTheAxisAndTheAngles()
    {
        Assert.True(GRepParserV2.TryCylinderArc(Cylinder(), [[1.0, 4.0], [1.5, 4.0], [2.0, 4.0]], out var arc));
        Assert.Equal([1.0, 2.0, 7.0], arc.Center);
        Assert.Equal([0.0, 0.0, 1.0], arc.Normal);
        Assert.Equal(1.0, arc.Start); Assert.Equal(2.0, arc.End);
    }

    [Fact]
    public void ACylinderArcWithAFallingAngleIsTheMirroredCircleWithAGrowingParameter()
    {
        Assert.True(GRepParserV2.TryCylinderArc(Cylinder(), [[2.0, 4.0], [1.5, 4.0], [1.0, 4.0]], out var arc));
        Assert.Equal([0.0, 0.0, -1.0], arc.Normal);
        // the reversed arc of the expected export: the range of the growing arc [1, 2] mirrored to [2 pi - 2, 2 pi - 1]
        Assert.Equal(2.0 * Math.PI - 2.0, arc.Start); Assert.Equal(2.0 * Math.PI - 1.0, arc.End);
        // the point of the parameter 2 pi - 2 on the mirrored circle is the point of the angle 2 on the cylinder
        var per = new[] { arc.Normal[1] * arc.Ref[2] - arc.Normal[2] * arc.Ref[1], arc.Normal[2] * arc.Ref[0] - arc.Normal[0] * arc.Ref[2], arc.Normal[0] * arc.Ref[1] - arc.Normal[1] * arc.Ref[0] };
        double x = arc.Center[0] + arc.Radius * (Math.Cos(arc.Start) * arc.Ref[0] + Math.Sin(arc.Start) * per[0]);
        double y = arc.Center[1] + arc.Radius * (Math.Cos(arc.Start) * arc.Ref[1] + Math.Sin(arc.Start) * per[1]);
        Assert.Equal(1 + 2 * Math.Cos(2.0), x, 12); Assert.Equal(2 + 2 * Math.Sin(2.0), y, 12);
    }

    [Fact]
    public void ACylinderArcTakesTheUnitFrameOfTheCylinder()
    {
        // Iso-parameter curve of a cylinder: the reference direction is the unit x vector and the normal the cross product of the unit x and y vectors,
        // whatever the length of the stored vectors
        var cylinder = Cylinder();
        cylinder.X = [2, 0, 0]; cylinder.Y = [0, 3, 0];
        Assert.True(GRepParserV2.TryCylinderArc(cylinder, [[1.0, 4.0], [1.5, 4.0], [2.0, 4.0]], out var arc));
        Assert.Equal([1.0, 0.0, 0.0], arc.Ref);
        Assert.Equal([0.0, 0.0, 1.0], arc.Normal);
        Assert.True(GRepParserV2.TryCylinderArc(cylinder, [[2.0, 4.0], [1.5, 4.0], [1.0, 4.0]], out arc));
        Assert.Equal([0.0, 0.0, -1.0], arc.Normal);
    }

    [Fact]
    public void AnEdgeBesideAnActiveCylinderIsTheCircleOfTheCylinderWhateverTheOtherFaceIs()
    {
        // The Edge is one object for both Faces: a revolved face, cone or second cylinder on the other side takes the circle of the cylinder, which
        // runs [2 pi - hi, 2 pi - lo] for a falling angle, as it does for a plane on the other side.
        const uint plane = 1, cylinder = 2;
        var surfaces = new Dictionary<uint, GRepParserV2.SurfaceRec>
        {
            [plane] = new() { Kind = GRepParserV2.SurfKind.Plane, X = [1, 0, 0], Y = [0, 1, 0] },
            [cylinder] = Cylinder(),
        };
        var edge = new GRepParserV2.EdgeRec
        {
            Handle = 7, FaceA = plane, FaceB = cylinder, HasEndpoints = true, Flags = 14,
            Start = new() { UB = 2.0, VB = 4.0 }, EndP = new() { UB = 1.0, VB = 4.0 },
        };
        edge.Interior.Add(new() { UB = 1.5, VB = 4.0 });

        Assert.True(GRepParserV2.TryCylinderNeighbourArc(edge, true, surfaces, new HashSet<uint> { plane, cylinder }, out var arc));
        Assert.Equal(2.0 * Math.PI - 2.0, arc.Start); Assert.Equal(2.0 * Math.PI - 1.0, arc.End);
        Assert.Equal([0.0, 0.0, -1.0], arc.Normal);
        Assert.True(GRepParserV2.TryArc(edge, true, surfaces[plane], surfaces, plane, out var seenFromPlane));
        Assert.Equal(arc.Start, seenFromPlane.Start); Assert.Equal(arc.End, seenFromPlane.End);
        // an inactive cylinder is no source of the edge curve
        Assert.False(GRepParserV2.TryCylinderNeighbourArc(edge, true, surfaces, new HashSet<uint> { plane }, out _));
    }

    private static GRepParserV2.EdgeRec PlaneCylinderEdge(double angleFrom, double angleTo) => new()
    {
        Handle = 1, FaceA = 10, FaceB = 20, HasEndpoints = true,
        Start = new GRepParserV2.EdgeUV { UB = angleFrom, VB = 4.0 }, EndP = new GRepParserV2.EdgeUV { UB = angleTo, VB = 4.0 },
    };

    [Theory]
    [InlineData(1.0, 2.0)]
    [InlineData(2.0, 1.0)]
    public void ThePlaneEdgeBesideACylinderTakesTheArcOfTheCylinderCircle(double from, double to)
    {
        var plane = new GRepParserV2.SurfaceRec { Kind = GRepParserV2.SurfKind.Plane, O = [0, 0, 4], X = [1, 0, 0], Y = [0, 1, 0] };
        var surfaces = new Dictionary<uint, GRepParserV2.SurfaceRec> { [10] = plane, [20] = Cylinder() };
        Assert.True(GRepParserV2.TryArc(PlaneCylinderEdge(from, to), true, plane, surfaces, 10, out var arc));
        Assert.Equal([1.0, 2.0, 7.0], arc.Center);
        Assert.Equal(2.0, arc.Radius);
        // a growing angle keeps its range; a falling one is the reversed arc of the expected export (the sample project A window arcs of the edges 28 and 30 carry exactly these numbers)
        Assert.Equal(from < to ? from : 2.0 * Math.PI - from, arc.Start);
        Assert.Equal(from < to ? to : 2.0 * Math.PI - to, arc.End);
        Assert.Equal(from < to ? 1.0 : -1.0, arc.Normal[2]);
    }

    // An edge: the first face of the edge that is active, a Hermite (NURBS) face included -- the sample project A windows take
    // 1170 vertices of the Hermite faces, none of which is any other evaluation than the Hermite one
    [Fact]
    public void TheEdgeIsEvaluatedOnItsFirstActiveFaceAHermiteFaceIncluded()
    {
        var surfaces = new Dictionary<uint, GRepParserV2.SurfaceRec>
        {
            [10] = new() { Kind = GRepParserV2.SurfKind.Hermite }, [20] = new() { Kind = GRepParserV2.SurfKind.Plane },
        };
        var edge = new GRepParserV2.EdgeRec { Handle = 1, FaceA = 10, FaceB = 20 };
        Assert.Equal(10u, GRepParserV2.MeshEdgeEvaluationFace(edge, 0, [10, 20], surfaces));
        Assert.Equal(20u, GRepParserV2.MeshEdgeEvaluationFace(edge, 0, [20], surfaces));
        Assert.Null(GRepParserV2.MeshEdgeEvaluationFace(edge, 0, [], surfaces));
    }

    // The legacy sampling replaces the stored points of a circular edge by the expected export's sampler; the face mesher reads the stored ones: the expected export evaluates the
    // vertex at the stored uv, and the plane side uv of the sampler (a projection) differs from them in the last bits (sample project A windows: arcs of 4 or 5 steps)
    [Fact]
    public void TheSamplingOfACircularEdgeKeepsTheStoredPointsForTheFaceMesher()
    {
        var cylinder = Cylinder();
        var plane = new GRepParserV2.SurfaceRec { Kind = GRepParserV2.SurfKind.Plane, O = [1, 2, 7], X = [1, 0, 0], Y = [0, 1, 0] };
        var stored = Enumerable.Range(0, 13).Select(i => new GRepParserV2.EdgeUV { UA = 0.5 + i * 0.1, VA = 4.0, UB = 2.0 * Math.Cos(0.5 + i * 0.1) + 1e-13, VB = 2.0 * Math.Sin(0.5 + i * 0.1) - 1e-13 }).ToList();
        var edge = new GRepParserV2.EdgeRec { Handle = 1, FaceA = 20, FaceB = 10, Flags = 14, HasEndpoints = true, Start = stored[0], EndP = stored[^1], Interior = stored.Skip(1).SkipLast(1).ToList() };
        Assert.NotNull(GRepParserV2.ResampleCircularEdge(edge, cylinder, plane));
        Assert.True(edge.Interior.Count < 11);
        Assert.NotEqual(stored[0].UB, edge.Start.UB);
        var authored = edge.AuthoredPoints();
        Assert.Equal(13, authored.Count);
        Assert.Equal(stored.Select(p => (p.UA, p.VA, p.UB, p.VB)), authored.Select(p => (p.UA, p.VA, p.UB, p.VB)));
        // an edge nobody replaced reports its own points
        var plain = new GRepParserV2.EdgeRec { Start = stored[0], EndP = stored[^1] };
        Assert.Equal(2, plain.AuthoredPoints().Count);
    }

    [Fact]
    public void ACylinderArcNeedsAConstantHeightAndASweep()
    {
        Assert.False(GRepParserV2.TryCylinderArc(Cylinder(), [[1.0, 4.0], [1.5, 4.5], [2.0, 4.0]], out _));
        Assert.False(GRepParserV2.TryCylinderArc(Cylinder(), [[1.0, 4.0], [1.0, 4.0]], out _));
    }

    private static List<double[]> OnCircle(double[] centre, double radius, double from, double to, int n) =>
        Enumerable.Range(0, n).Select(i =>
        {
            double t = from + (to - from) * i / (n - 1);
            return new[] { centre[0] + radius * Math.Cos(t), centre[1] + radius * Math.Sin(t), centre[2] };
        }).ToList();

    [Fact]
    public void ThreeOrMoreSamplesOnACircleGiveTheCircleRunningFromTheFirstSample()
    {
        var points = OnCircle([1, 2, 3], 0.5, 0.3, 2.1, 7);
        Assert.True(GRepParserV2.TryFitCircle(points, out var arc));
        Assert.Equal(0.5, arc.Radius, 12);
        for (int k = 0; k < 3; k++) Assert.Equal(new[] { 1.0, 2.0, 3.0 }[k], arc.Center[k], 12);
        Assert.Equal(0.0, arc.Start); Assert.Equal(1.8, arc.End, 9);
    }

    [Fact]
    public void AClockwiseArcIsTheMirroredCircle()
    {
        var points = OnCircle([0, 0, 0], 1, 2.0, 0.5, 6);
        Assert.True(GRepParserV2.TryFitCircle(points, out var arc));
        Assert.Equal(-1.0, arc.Normal[2], 12);
        Assert.Equal(1.5, arc.End, 9);
    }

    [Fact]
    public void SamplesOnAnEllipseOrWithoutThreeDistinctPointsAreNotACircle()
    {
        var ellipse = OnCircle([0, 0, 0], 1, 0, 1.5, 9).Select(p => new[] { p[0] * 1.2, p[1], p[2] }).ToList();
        Assert.False(GRepParserV2.TryFitCircle(ellipse, out _));
        Assert.False(GRepParserV2.TryFitCircle([[0, 0, 0], [1, 0, 0]], out _));
        Assert.False(GRepParserV2.TryFitCircle([[0, 0, 0], [1, 0, 0], [2, 0, 0]], out _));
    }

    [Fact]
    public void TheAuthoredSamplesOfAnEdgeAreUsedOnceWhereTwoPiecesMeet()
    {
        var uv = GRepParserV2.DistinctUv([[0, 0], [1, 0], [1, 0], [2, 1], [2, 1], [3, 1]]);
        Assert.Equal(4, uv.Length);
        Assert.Equal(new[] { 0.0, 1.0, 2.0, 3.0 }, uv.Select(p => p[0]));
        Assert.Equal(2, GRepParserV2.DistinctUv([[0, 0], [0, 0]]).Length);
    }

    // a torus patch: the profile circle of radius 0.5 around (3, 0, 0) of the local xz plane revolved about the frame axis
    private static (GRepParserV2.SurfaceRec Surface, GRepParserV2.ArcRec Profile) Torus() => (
        new() { Kind = GRepParserV2.SurfKind.Rev, O = [10, 20, 30], X = [1, 0, 0], Y = [0, 1, 0], Z = [0, 0, 1], Env = [0, 0, 1.5, Math.PI] },
        new() { C = [3, 0, 0], X = [1, 0, 0], Y = [0, 0, 1], R = 0.5 });

    [Fact]
    public void ARevolvedSurfaceGetsTheFrameAxisAndTheProfileCircleInTheWorld()
    {
        var (surface, profile) = Torus();
        Assert.True(GRepParserV2.MeshProfileSupported(profile));
        var s = GRepParserV2.MeshRevolvedSurfaceOf(surface, profile, false);
        Assert.Equal([0.0, 0.0, 1.0], s.Axis);
        Assert.Equal([13.0, 20.0, 30.0], s.ArcCenter);
        Assert.Equal(0.5, s.ArcRadius);
        Assert.Equal([0.0, Math.PI, 0.0, 1.5], s.GeEnvelope);
        // a surface point of the reader against the model: rotation 0.7, profile parameter 1.1
        var p = GRepParserV2.MeshRevolvedPoint(surface, profile, 0.7, 1.1);
        double r = 3 + 0.5 * Math.Cos(1.1);
        Assert.Equal(10 + r * Math.Cos(0.7), p[0], 12);
        Assert.Equal(20 + r * Math.Sin(0.7), p[1], 12);
        Assert.Equal(30 + 0.5 * Math.Sin(1.1), p[2], 12);
    }

    [Fact]
    public void ALeftHandedRevolvedFrameTurnsAboutZWithAMirroredRotation()
    {
        var (surface, profile) = Torus();
        var right = GRepParserV2.MeshRevolvedSurfaceOf(surface, profile, false);
        Assert.False(right.NormalReversed);
        // Z against X x Y: the expected export sees the surface of a hole (isNormalReversed), turns about Z and negates the rotation range
        surface.Z = [0, 0, -1];
        var left = GRepParserV2.MeshRevolvedSurfaceOf(surface, profile, false);
        Assert.True(left.NormalReversed);
        Assert.Equal([0.0, 0.0, -1.0], left.Axis);
        Assert.Equal([-1.5, 0.0], new[] { left.GeEnvelope[2], left.GeEnvelope[3] });
    }

    [Fact]
    public void ARevolvedProfileThatIsNeitherALineNorACircleIsEvaluatedThroughTheCurve()
    {
        var (surface, profile) = Torus();
        profile.RY = 0.8;   // an elliptical arc
        Assert.False(GRepParserV2.MeshProfileIsLineOrCircle(profile));
        Assert.True(GRepParserV2.MeshProfileSupported(profile));
        var s = GRepParserV2.MeshRevolvedSurfaceOf(surface, profile, false);
        Assert.NotNull(s.ProfileAt);
        // the profile point of the parameter 1.1 at the rotation 0: the local (x, ., z) of the curve placed on the frame
        var local = profile.Eval(1.1);
        var world = s.ProfileAt!(1.1);
        Assert.Equal(10 + local[0], world[0], 12);
        Assert.Equal(20, world[1], 12);
        Assert.Equal(30 + local[2], world[2], 12);
        // a curve of a single control point is no curve
        profile.RY = 0; profile.NurbControlPoints = [new GRepParserV2.NurbControlPoint()];
        Assert.False(GRepParserV2.MeshProfileSupported(profile));
    }

    [Fact]
    public void TheEdgesOfARevolvedFaceAreTheProfileAtAFixedRotationOrACircleAroundTheAxis()
    {
        var (surface, profile) = Torus();
        // a meridian edge (rotation 0.7, profile 0.2 .. 1.4)
        var meridian = new List<double[]> { new[] { 0.7, 0.2 }, new[] { 0.7, 0.8 }, new[] { 0.7, 1.4 } };
        var pts = meridian.Select(p => GRepParserV2.MeshRevolvedPoint(surface, profile, p[0], p[1])).ToList();
        Assert.True(GRepParserV2.TryRevolvedCurve(surface, profile, meridian, pts, out var curve, out _, out _));
        Assert.Equal(CurveKind.Arc, curve.Kind);
        Assert.Equal(0.5, curve.Arc!.Radius, 12);
        Assert.Equal(0.2, curve.Arc.Start, 12); Assert.Equal(1.4, curve.Arc.End, 12);
        // a parallel edge (profile 0.2, rotation 1.2 .. 0.4: falling, so the circle is mirrored)
        var parallel = new List<double[]> { new[] { 1.2, 0.2 }, new[] { 0.8, 0.2 }, new[] { 0.4, 0.2 } };
        pts = parallel.Select(p => GRepParserV2.MeshRevolvedPoint(surface, profile, p[0], p[1])).ToList();
        Assert.True(GRepParserV2.TryRevolvedCurve(surface, profile, parallel, pts, out curve, out _, out _));
        Assert.Equal(3 + 0.5 * Math.Cos(0.2), curve.Arc!.Radius, 12);
        Assert.Equal(-1.0, curve.Arc.Normal[2], 12);
        Assert.Equal(-1.2, curve.Arc.Start, 12); Assert.Equal(-0.4, curve.Arc.End, 12);
        // an edge that changes both parameters is not described
        var oblique = new List<double[]> { new[] { 0.1, 0.2 }, new[] { 0.5, 0.6 } };
        Assert.False(GRepParserV2.TryRevolvedCurve(surface, profile, oblique, pts.Take(2).ToList(), out _, out _, out _));
    }

    [Fact]
    public void AProfileLineAndArcOfARuledSurfaceKeepTheirParameterInterval()
    {
        var line = new GRepParserV2.ArcRec { C = [1, 2, 3], X = [0, 0, 1], T0 = 0.5, T1 = 2.5 };
        var l = GRepParserV2.MeshProfileCurve(line)!;
        Assert.Equal(CurveKind.Line, l.Kind);
        Assert.Equal([1.0, 2.0, 3.5], l.P0); Assert.Equal([1.0, 2.0, 5.5], l.P1);
        var arc = new GRepParserV2.ArcRec { C = [0, 0, 0], X = [1, 0, 0], Y = [0, 1, 0], R = 2, T0 = 0, T1 = 1.5 };
        var a = GRepParserV2.MeshProfileCurve(arc)!;
        Assert.Equal(CurveKind.Arc, a.Kind);
        Assert.Equal(2.0, a.Arc!.Radius); Assert.Equal([0.0, 0.0, 1.0], a.Arc.Normal);
        arc.RY = 1;
        Assert.Equal(CurveKind.Ellipse, GRepParserV2.MeshProfileCurve(arc)!.Kind);
    }

    [Fact]
    public void AFreeFormEdgeIsALineACircleOrTheSplineWithTheUvOnTheSameKnots()
    {
        var e = new GRepParserV2.EdgeRec { Flags = 6 };
        var straight = new List<double[]> { new[] { 0.0, 0, 0 }, new[] { 1.0, 0, 0 }, new[] { 2.0, 0, 0 } };
        var uvs = new List<double[]> { new[] { 0.0, 0 }, new[] { 0.5, 0 }, new[] { 1.0, 0 } };
        Assert.True(GRepParserV2.TryFreeformEdge(e, straight, uvs, out var curve, out var pc, out _, out _));
        Assert.Equal(CurveKind.Line, curve.Kind); Assert.Equal([1.0, 0.0], pc!.P1);
        // a bent edge with duplicated samples: the spline has one control point more than two per distinct sample
        var bent = new List<double[]>(); var bentUv = new List<double[]>();
        for (int i = 0; i < 6; i++)
        {
            var p = new[] { i * 0.3, Math.Sin(i * 0.4) * 0.2, 0.0 };
            bent.Add(p); bent.Add(p); bentUv.Add([i * 0.1, i * 0.05]); bentUv.Add([i * 0.1, i * 0.05]);
        }
        Assert.True(GRepParserV2.TryFreeformEdge(e, bent, bentUv, out curve, out pc, out double lo, out double hi));
        Assert.Equal(CurveKind.Nurbs, curve.Kind);
        Assert.Equal(8, curve.Nurbs!.ControlPoints.Length);
        Assert.Equal(curve.Nurbs.Knots, pc!.Nurbs!.Knots);
        Assert.Equal(lo, pc.Lo); Assert.Equal(hi, pc.Hi);
    }
}
