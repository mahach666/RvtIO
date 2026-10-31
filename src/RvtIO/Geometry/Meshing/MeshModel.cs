namespace RvtIO.Geometry.Meshing;

internal enum SurfaceKind { Plane, Cylinder, Cone, Revolved, Ruled, NurbsSurface }

internal enum CurveKind { Line, Arc, Nurbs, Ellipse }

/// <summary>Plane: origin and the u and v vectors of the surface frame (not necessarily unit vectors).</summary>
internal sealed class PlaneSurface
{
    public required double[] Origin { get; init; }
    public required double[] U { get; init; }
    public required double[] V { get; init; }
    public double[]? Normal { get; init; }
}

/// <summary>
/// Cylinder: the frame and parameter box the cylinder mesher reads. x = axial / UScale, y = angle; the face's coedges carry V1,
/// V2, ParamV1, ParamV2, EdgeOrientToCurve, OrientToLoop, EdgeMarker, Curve and optionally ParamCurve (non rational NURBS).
/// </summary>
internal sealed class CylinderSurface
{
    public required double Radius { get; init; }
    public required double[] Origin { get; init; }
    public required double[] Axis { get; init; }
    public required double[] Ref { get; init; }
    /// <summary>Signed axial scale, negative for the faces of hole surfaces.</summary>
    public required double UScale { get; init; }
    public required double AngleLo { get; init; }
    public required double AngleHi { get; init; }
    public required double HeightLo { get; init; }
    public required double HeightHi { get; init; }
    public bool Outer { get; init; }
    /// <summary>Not zero for a cone: Origin is then the apex and UScale scales the slant distance from the apex.</summary>
    public double ConeHalfAngle { get; init; }
}

/// <summary>
/// Revolved surface (cone, truncated cone, sphere, torus, elbow, any line or circular arc profile): the surface the B-rep
/// mesher samples, S(u, v) with u = profile parameter and v = rotation angle (the Compact face stores them the other way
/// round).
/// </summary>
internal sealed class RevolvedSurface
{
    public required double[] Origin { get; init; }
    public required double[] Axis { get; init; }
    public required double V0 { get; init; }
    public required double U0 { get; init; }
    public double[]? ProfileStart { get; init; }
    public double[]? ProfileDir { get; init; }
    public double[]? ArcCenter { get; init; }
    public double[]? ArcE1 { get; init; }
    public double[]? ArcE2 { get; init; }
    public double ArcRadius { get; init; }
    public double ArcEps { get; init; } = 1.0;
    /// <summary>Any other profile (an elliptical arc, a NURBS or Hermite curve): the world point of the profile at the parameter u for the rotation V0.</summary>
    public Func<double, double[]>? ProfileAt { get; init; }
    /// <summary>[uLo, uHi, vLo, vHi] in the mesher order (u = profile, v = rotation).</summary>
    public required double[] GeEnvelope { get; init; }
    /// <summary>The surface normal is reversed: the rotation of the edge uv is negated; together with Flipped it decides the winding.</summary>
    public bool NormalReversed { get; init; }
}

/// <summary>
/// Ruled surface: S(u, v) = (1 - v) C0(u) + v C1(u) with u the fraction of the parameter interval of the profile curve. A
/// profile that is null is a point (Point0 / Point1).
/// </summary>
internal sealed class RuledSurface
{
    public MeshCurve? Profile0 { get; init; }
    public MeshCurve? Profile1 { get; init; }
    public double[]? Point0 { get; init; }
    public double[]? Point1 { get; init; }
}

/// <summary>
/// NURBS surface: non-rational tensor product B-spline surface, control points in u-major order (index iu * nV + iv). The
/// parameter domain is [KnotsU[DegreeU], KnotsU[nU]] x [KnotsV[DegreeV], KnotsV[nV]] (the stored envelope equals it).
/// </summary>
internal sealed class NurbsSurface
{
    public required int DegreeU { get; init; }
    public required int DegreeV { get; init; }
    public required double[] KnotsU { get; init; }
    public required double[] KnotsV { get; init; }
    public required double[][] ControlPoints { get; init; }
    public double[]? Weights { get; init; }
}

internal sealed class Nurbs
{
    public required int Degree { get; init; }
    public required double[] Knots { get; init; }
    public required double[][] ControlPoints { get; init; }
    public double[]? Weights { get; init; }
}

/// <summary>Circular arc parameters: P(t) = Center + R cos(t) Ref + R sin(t) (Normal x Ref).</summary>
internal sealed class Arc
{
    public required double[] Center { get; init; }
    public required double Radius { get; init; }
    public required double[] Normal { get; init; }
    public required double[] Ref { get; init; }
    public required double Start { get; init; }
    public required double End { get; init; }
    /// <summary>The parameter range of the edge on the circle when it is known exactly (the stored rotation range of a revolved edge); the sampler
    /// then uses it instead of measuring the vertices again, whose rounding noise decides the interval count of an exact half circle.</summary>
    public (double From, double To)? EdgeRange { get; init; }
}

/// <summary>Elliptical arc: P(t) = Center + MajorRadius cos(t) MajorAxis + MinorRadius sin(t) MinorAxis (unit axes).</summary>
internal sealed class Ellipse
{
    public required double[] Center { get; init; }
    public required double[] MajorAxis { get; init; }
    public required double[] MinorAxis { get; init; }
    public required double MajorRadius { get; init; }
    public required double MinorRadius { get; init; }
    public required double Start { get; init; }
    public required double End { get; init; }
}

/// <summary>
/// An edge curve of the B-rep section (line, arc, ellipse or NURBS) with its parameter interval and end points. A line runs
/// from P0 (parameter Lo) to P1 (parameter Hi).
/// </summary>
internal sealed class MeshCurve
{
    public required CurveKind Kind { get; init; }
    public required double Lo { get; init; }
    public required double Hi { get; init; }
    public double[]? P0 { get; init; }
    public double[]? P1 { get; init; }
    public Arc? Arc { get; init; }
    public Nurbs? Nurbs { get; init; }
    public Ellipse? Ellipse { get; init; }
}

/// <summary>
/// The 2D parameter curve of an edge on its face surface (cylinder and freeform tracks). Lo and Hi are the parameters of the
/// edge curve it is parametrised by.
/// </summary>
internal sealed class ParamCurve
{
    public double Lo { get; init; }
    public double Hi { get; init; }
    public double[]? P0 { get; init; }
    public double[]? P1 { get; init; }
    public Nurbs? Nurbs { get; init; }
    /// <summary>
    /// The curve is only a starting guess: the uv of an edge sample is the closest point of the surface (the mesher's own
    /// fallback for a parameter curve that fails its validity test), so the end point uv of an edge are enough.
    /// </summary>
    public bool Projected { get; init; }
}

internal sealed class MeshCoedge
{
    public int EdgeMarker { get; init; } = -1;
    public bool OrientToLoop { get; init; } = true;
    public bool EdgeOrientToCurve { get; init; } = true;
    public double[]? V1 { get; init; }
    public double[]? V2 { get; init; }
    public double ParamV1 { get; init; }
    public double ParamV2 { get; init; }
    public MeshCurve? Curve { get; init; }
    public MeshCurve? OrientedCurve { get; init; }
    public ParamCurve? ParamCurve { get; init; }

    /// <summary>First and last point of the coedge, 3D, in loop order.</summary>
    public double[]? Start { get; init; }
    public double[]? End { get; init; }
    /// <summary>Sample points of a curved coedge including both ends (3D, loop order); null for a straight coedge, which the
    /// mesher divides itself. The arc sampler is ArcSampler.</summary>
    public double[][]? Samples { get; init; }
    /// <summary>First and last uv of the coedge on this face, loop order; null when not stored (the projection of the 3D point is used).</summary>
    public double[]? UvStart { get; init; }
    public double[]? UvEnd { get; init; }
    /// <summary>
    /// Revolved faces: the uv of the edge on this face in the direction of the edge curve. UvStart and UvEnd then hold the ends
    /// in loop order.
    /// </summary>
    public double[][]? UvFit { get; init; }
}

internal sealed class Loop
{
    public required IReadOnlyList<MeshCoedge> Coedges { get; init; }
}

internal sealed class BrFace
{
    /// <summary>Marker of the B-rep face (Compact face tag + 1 for tagged faces).</summary>
    public int Marker { get; init; }
    public required SurfaceKind SurfaceKind { get; init; }
    /// <summary>B-rep orientToSurface.</summary>
    public bool OrientToSurface { get; init; } = true;
    /// <summary>The Compact face flipped flag (decides the winding of the planar mesh).</summary>
    public bool Flipped { get; init; }
    /// <summary>Stored face envelope [u0, v0, u1, v1]; the planar rule does not read it.</summary>
    public double[]? Envelope { get; init; }
    public PlaneSurface? Plane { get; init; }
    public CylinderSurface? Cylinder { get; init; }
    public RevolvedSurface? Revolved { get; init; }
    public RuledSurface? Ruled { get; init; }
    public NurbsSurface? Nurbs { get; init; }
    /// <summary>The faces of the solid this face belongs to (this face included).</summary>
    public IReadOnlyList<BrFace>? Solid { get; set; }
    public required IReadOnlyList<Loop> Loops { get; init; }
}

internal sealed class Mesh
{
    public required double[][] Vertices { get; init; }
    public required int[][] Triangles { get; init; }
}
