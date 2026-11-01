using RvtIO.Geometry.Meshing;

namespace RvtIO.Parsing;

internal static partial class GRepParserV2
{
    /// <summary>The area <c>computeAreaOfFace</c> gives each planar Face with curved Edges, and each cylindrical Face: the
    /// quadrature of <see cref="FaceArea"/> over the coedges of its loops in file order, with the Edge points as the file stores them.
    /// A planar Face of straight Edges has no entry: the quadrature of a line is its exact value, the polygon area. A Face with an open loop or
    /// an Edge that is not known on both sides has none either.</summary>
    private static Dictionary<uint, double> ComputeFaceAreas(Cur c, Dictionary<uint, SurfaceRec> surfaces, IEnumerable<uint> faces,
        Dictionary<uint, List<int>> loopsByFace, Dictionary<uint, EdgeRec?> edges)
    {
        var result = new Dictionary<uint, double>();
        foreach (uint face in faces)
        {
            if (!surfaces.TryGetValue(face, out var surface) || surface.Kind is not (SurfKind.Plane or SurfKind.Cylinder)
                || surface.Kind == SurfKind.Cylinder && !c.IncludeNonVisible
                || !loopsByFace.TryGetValue(face, out var indices) || indices.Count == 0)
                continue;
            var coedges = SurfaceCoedges(c, face, indices, edges);
            bool curved = coedges?.Any(edge => edge.Points.Count > 2) == true;
            if (coedges != null && (curved || surface.Kind == SurfKind.Cylinder)
                && FaceArea.Compute(coedges) is { } area)
                result[face] = surface.Kind == SurfKind.Cylinder ? area * Math.Abs(surface.R) : area;
        }
        return result;
    }

    private static List<FaceArea.Coedge>? SurfaceCoedges(Cur c, uint face, List<int> indices,
        Dictionary<uint, EdgeRec?> edges)
    {
        var coedges = new List<FaceArea.Coedge>();
        foreach (int index in indices)
        {
            var loop = c.SpaceEdgeLoops[index];
            uint sentinel = (uint)c.Queue[index].V, current = loop.Next;
            int guard = 0;
            if (loop.Open != 0) return null;
            while (current != sentinel)
            {
                if (guard++ > 100000 || !edges.TryGetValue(current, out var edge) || edge == null || !edge.HasEndpoints
                    || edge.FaceA == edge.FaceB || edge.FaceA != face && edge.FaceB != face)
                    return null;
                bool firstFace = edge.FaceA == face;
                var points = edge.AuthoredPoints().Select(point => firstFace ? (point.UA, point.VA) : (point.UB, point.VB)).ToList();
                coedges.Add(new FaceArea.Coedge(points, edge.Flags, !firstFace));
                current = firstFace ? edge.NextA : edge.NextB;
            }
        }
        return coedges;
    }
}
