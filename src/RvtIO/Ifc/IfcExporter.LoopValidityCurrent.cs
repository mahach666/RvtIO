using RvtIO.Model;

namespace RvtIO.Ifc;

/// <summary>Judgements of the loops of an extrusion face after the pair of faces has been found: the
/// extrusion is only made when the orientation of the loops of the lower face is acceptable.</summary>
public partial class IfcExporter
{
    // Tolerance of the curve intersector and of matrix comparison.
    private const double OthersCurrentGeTolerance = 1e-10;

    /// <summary>The edges of a loop are appended to a curve loop one by one, and an edge is taken back out when the loop then
    /// intersects itself (every pair of its curves is compared, neighbours included). The loop stays open, the extruded solid
    /// is not created and the body is a Brep (for instance the cap polygon of a panel that crosses itself).
    /// Faces with circular edges are not judged (their polygons are tessellations).</summary>
    private static bool OthersCurrentLoopsIntersectThemselves(OthersCurrentFace face)
    {
        if (face.Regions.Any(region => region.MeshArcs is { Count: > 0 })) return false;
        IEnumerable<IReadOnlyList<double[]>> rings = face.Regions[0].SourceLoops is { Count: > 0 } sourceLoops
            ? sourceLoops
            : face.Regions.SelectMany(region => new[] { region.OuterVertices }.Concat(region.InnerVertices ?? []))
                .Where(ring => ring != null).Select(ring => (IReadOnlyList<double[]>)ring!);
        return rings.Any(OthersCurrentRingIntersectsItself);
    }

    internal static bool OthersCurrentRingIntersectsItself(IReadOnlyList<double[]> ring)
    {
        var vertices = new List<double[]>();
        foreach (var point in ring)
            if (point.Length >= 3 && (vertices.Count == 0 || OthersCurrentDistance(vertices[^1], point) > OthersCurrentSameVertex)) vertices.Add(point);
        if (vertices.Count > 1 && OthersCurrentDistance(vertices[0], vertices[^1]) <= OthersCurrentSameVertex) vertices.RemoveAt(vertices.Count - 1);
        int count = vertices.Count;
        if (count < 3) return false;
        for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
                if (OthersCurrentSegmentsIntersect(vertices[i], vertices[(i + 1) % count], vertices[j], vertices[(j + 1) % count])) return true;
        return false;
    }

    // An intersection whose parameter on the first curve is within this of the end of that curve that coincides with an end point
    // of the second one is dropped.
    private const double OthersCurrentEndParameterTolerance = 1e-9;

    /// <summary>Whether two segments of a loop count as intersecting: they come within the tolerance of each other, except where
    /// they meet only in end points that coincide (neighbours, a vertex the loop visits twice) or where one runs back along the
    /// other from a shared end point. The first curve of the pair is the earlier one of the loop.</summary>
    internal static bool OthersCurrentSegmentsIntersect(double[] a0, double[] a1, double[] b0, double[] b1)
    {
        var u = OthersCurrentSub(a1, a0);
        double lengthA = OthersCurrentLength(u);
        if (lengthA <= 0) return false;
        double distance = OthersCurrentSegmentDistance(a0, a1, b0, b1, out double s, out _);
        if (distance >= OthersCurrentGeTolerance) return false; // the comparison is strict: a contact at exactly 1e-10 is none

        // The contact as an interval [low, high] of the length along the first segment.
        double low, high;
        var unit = new[] { u[0] / lengthA, u[1] / lengthA, u[2] / lengthA };
        double Along(double[] p) => OthersCurrentDot(OthersCurrentSub(p, a0), unit);
        double Off(double[] p) => OthersCurrentLength(OthersCurrentCross(OthersCurrentSub(p, a0), unit));
        if (Off(b0) <= OthersCurrentGeTolerance && Off(b1) <= OthersCurrentGeTolerance)
        {
            // The second segment lies along the first: they overlap on the part of it the other covers.
            double p0 = Along(b0), p1 = Along(b1);
            low = Math.Max(0, Math.Min(p0, p1));
            high = Math.Min(lengthA, Math.Max(p0, p1));
            if (low > high) low = high = Math.Clamp(low, 0, lengthA);
        }
        else low = high = s * lengthA;

        bool startMeets = OthersCurrentDistance(a0, b0) <= OthersCurrentGeTolerance || OthersCurrentDistance(a0, b1) <= OthersCurrentGeTolerance;
        bool endMeets = OthersCurrentDistance(a1, b0) <= OthersCurrentGeTolerance || OthersCurrentDistance(a1, b1) <= OthersCurrentGeTolerance;
        return !(startMeets && low <= OthersCurrentEndParameterTolerance) && !(endMeets && high >= lengthA - OthersCurrentEndParameterTolerance);
    }

    /// <summary>The distance between two 3D segments (Ericson, Real-Time Collision Detection 5.1.9) and the parameters (0..1) of the
    /// nearest points.</summary>
    internal static double OthersCurrentSegmentDistance(double[] p1, double[] q1, double[] p2, double[] q2, out double s, out double t)
    {
        var d1 = OthersCurrentSub(q1, p1);
        var d2 = OthersCurrentSub(q2, p2);
        var r = OthersCurrentSub(p1, p2);
        double a = OthersCurrentDot(d1, d1), e = OthersCurrentDot(d2, d2), f = OthersCurrentDot(d2, r);
        if (a <= 1e-30 && e <= 1e-30)
        {
            s = t = 0;
            return OthersCurrentLength(r);
        }
        if (a <= 1e-30)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            double c = OthersCurrentDot(d1, r);
            if (e <= 1e-30)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                double b = OthersCurrentDot(d1, d2);
                double denominator = a * e - b * b;
                s = denominator > 1e-30 ? Math.Clamp((b * f - c * e) / denominator, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }
        var c1 = new[] { p1[0] + d1[0] * s, p1[1] + d1[1] * s, p1[2] + d1[2] * s };
        var c2 = new[] { p2[0] + d2[0] * t, p2[1] + d2[1] * t, p2[2] + d2[2] * t };
        return OthersCurrentDistance(c1, c2);
    }

    /// <summary>The extruded solid is not made when the frame of the loop is the identity. The frame is that of the plane of
    /// the first loop of the lower face, turned counter-clockwise about the extrusion axis: its origin is the centroid of the
    /// loop (the mean of the start points of the curves) and its axes are the canonical ones of the normal, so for z it is the
    /// identity when the loop stands in the plane z = 0 and is centred on the origin of the family: a box or a prism about the
    /// origin of its family is a Brep. Loops with circular edges are not judged.</summary>
    private static bool OthersCurrentFrameIsIdentity(OthersCurrentFace lower, double[] axis)
    {
        if (axis[0] != 0 || axis[1] != 0 || axis[2] != 1) return false;
        if (lower.Regions.Any(item => item.MeshArcs is { Count: > 0 })) return false;
        IReadOnlyList<double[]>? ring = lower.Regions[0].FirstSourceLoop ?? lower.Regions[0].OuterVertices;
        if (ring is not { Count: >= 3 }) return false;
        var vertices = new List<double[]>();
        foreach (var point in ring)
            if (point.Length >= 3 && (vertices.Count == 0 || OthersCurrentDistance(vertices[^1], point) > OthersCurrentSameVertex)) vertices.Add(point);
        if (vertices.Count > 1 && OthersCurrentDistance(vertices[0], vertices[^1]) <= OthersCurrentSameVertex) vertices.RemoveAt(vertices.Count - 1);
        if (vertices.Count < 3) return false;
        var mean = OthersCurrentMean(vertices);
        return Math.Abs(mean[0]) <= OthersCurrentGeTolerance && Math.Abs(mean[1]) <= OthersCurrentGeTolerance && Math.Abs(mean[2]) <= OthersCurrentGeTolerance;
    }

    /// <summary>A serialized solid whose Edge records do not close it but whose polygons are an exact closed shell: its volume
    /// is computed and it is written as a Brep, not as the IfcFaceBasedSurfaceModel of a body without volume. Only the
    /// faces of the solid are judged, and all of them must be in orientable closed components.</summary>
    internal static bool IsExactClosedDespiteOpenTopologyCurrent(GeometryData geo, GeoBrepSolid solid, bool preserveValidatedPlanarFaces)
    {
        var indices = solid.FaceIndices.Distinct().Where(index => (uint)index < geo.Faces.Count).Order().ToList();
        if (indices.Count < 4) return false;
        var components = GetExactFaceComponents(geo.Faces, indices);
        return components.Count > 0 && components.Sum(component => component.Count) == indices.Count
            && components.All(component => TryOrientExactFacetedShell(geo.Faces, component, out _, triangulateNonPlanarFaces: !preserveValidatedPlanarFaces));
    }
}
