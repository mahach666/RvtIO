using RvtIO.Geometry.Meshing;

namespace RvtIO.Parsing;

internal static partial class GRepParserV2
{
    /// <summary>Area and SIGNED volume (negative when the faces point into the body, as the ones of a cutting body do) of a Geometry node
    /// made of planes, cylinders and cones.</summary>
    // shortcut: unsupported surface kinds retain polygon quantities until their surface integrals are decoded.
    private static (double Area, double Volume)? MeasureAnalyticSolid(Cur c, IEnumerable<uint> handles,
        Dictionary<uint, SurfaceRec> surfaces, Dictionary<uint, List<int>> loopsByFace,
        Dictionary<uint, EdgeRec?> edges)
    {
        var faces = handles.ToList();
        if (faces.Count == 0 || !surfaces.TryGetValue(faces[0], out var first)
            || first.Env is not { Length: 4 } envelope
            || MeshEvaluate(c, faces[0], first, (envelope[0] + envelope[2]) / 2,
                (envelope[1] + envelope[3]) / 2) is not { } reference)
            return null;

        double area = 0, volume = 0;
        foreach (uint handle in faces)
        {
            if (!surfaces.TryGetValue(handle, out var surface)
                || surface.Kind is not (SurfKind.Plane or SurfKind.Cylinder or SurfKind.Cone)
                || !loopsByFace.TryGetValue(handle, out var loops)
                || SurfaceCoedges(c, handle, loops, edges) is not { Count: > 0 } coedges)
                return null;

            if (MeasureAnalyticFace(surface, reference, coedges) is not { } measure) return null;
            double a = measure.Area, v = measure.Volume;
            area += a;
            volume += v;
        }
        return double.IsFinite(area) && double.IsFinite(volume) ? (area, volume) : null;
    }
    /// <summary>The Geometry nodes that are closed shells: every serialized Edge of a Face of the node joins two Faces of that node, so the
    /// signed volume of its faces means something (an open sheet has none).</summary>
    internal static HashSet<int> ClosedNodes(IEnumerable<(uint FaceA, uint FaceB)> edges, IReadOnlyDictionary<uint, (int Node, int Style)> nodeByFace)
    {
        static bool Valid(uint handle) => handle != 0 && handle != 0xFFFFFFFF;
        var touched = new HashSet<int>();
        var open = new HashSet<int>();
        foreach (var edge in edges)
        {
            (int Node, int Style) ownerA = default, ownerB = default;
            bool a = Valid(edge.FaceA) && nodeByFace.TryGetValue(edge.FaceA, out ownerA);
            bool b = Valid(edge.FaceB) && nodeByFace.TryGetValue(edge.FaceB, out ownerB);
            int nodeA = ownerA.Node, nodeB = ownerB.Node;
            if (a) touched.Add(nodeA);
            if (b) touched.Add(nodeB);
            if (a && b && nodeA == nodeB) continue;
            if (a) open.Add(nodeA);
            if (b) open.Add(nodeB);
        }
        touched.ExceptWith(open);
        return touched;
    }

    internal static (double Area, double Volume)? MeasureAnalyticFace(SurfaceRec surface, double[] reference,
        IReadOnlyList<FaceArea.Coedge> coedges)
    {
        if (surface.Kind is not (SurfKind.Plane or SurfKind.Cylinder or SurfKind.Cone)
            || reference.Length != 3 || coedges.Count == 0) return null;
        double[] delta = [reference[0] - surface.O[0], reference[1] - surface.O[1], reference[2] - surface.O[2]];
        double x = Dot3(delta, surface.X), y = Dot3(delta, surface.Y), z = Dot3(delta, surface.Z);
        double normal = Dot3(delta, MeshCross(surface.X, surface.Y)) * (surface.Orient == 0 ? -1 : 1);
        double handedness = Dot3(surface.X, MeshCross(surface.Y, surface.Z)) < 0 ? -1 : 1;
        double radius = surface.R, sine = Math.Sin(radius), cosine = Math.Cos(radius);

        double? faceArea = surface.Kind switch
        {
            SurfKind.Plane => FaceArea.Compute(coedges),
            SurfKind.Cylinder => FaceArea.Compute(coedges) * radius,
            _ => FaceArea.Integrate(coedges, (u, v, du, dv) => sine * u * v * dv)
                is { } coneArea ? Math.Abs(coneArea) : null,
        };
        double? faceVolume = FaceArea.Integrate(coedges, (u, v, du, dv) => surface.Kind switch
        {
            SurfKind.Plane => normal * v / 3 * du,
            SurfKind.Cylinder => handedness * ((Math.Sin(u) * y + Math.Cos(u) * x - radius) * v) * radius / 3 * du,
            _ => handedness * ((Math.Cos(u) * y - Math.Sin(u) * x) * cosine * sine + sine * sine * z * u) * v / 3 * dv,
        });
        return faceArea is { } a && faceVolume is { } v && double.IsFinite(a) && a >= 0 ? (a, v) : null;
    }

}
