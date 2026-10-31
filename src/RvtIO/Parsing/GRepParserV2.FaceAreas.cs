using RvtIO.Geometry.Meshing;

namespace RvtIO.Parsing;

internal static partial class GRepParserV2
{
    /// <summary>
    /// The area <c>computeAreaOfFace</c> gives each planar Face that has an Edge with interior
    /// points (an arc or a spline): the quadrature of <see cref="FaceArea"/> over the coedges of
    /// its loops in file order, with the Edge points as the file stores them.
    /// </summary>
    private static Dictionary<uint, double> ComputeFaceAreas(Cur c, Dictionary<uint, SurfaceRec> surfaces, IEnumerable<uint> faces,
        Dictionary<uint, List<int>> loopsByFace, Dictionary<uint, EdgeRec?> edges)
    {
        var result = new Dictionary<uint, double>();
        foreach (uint face in faces)
        {
            if (!surfaces.TryGetValue(face, out var surface) || surface.Kind != SurfKind.Plane
                || !loopsByFace.TryGetValue(face, out var indices) || indices.Count == 0)
                continue;
            var coedges = new List<FaceArea.Coedge>();
            bool curved = false, complete = true;
            foreach (int index in indices)
            {
                var loop = c.SpaceEdgeLoops[index];
                uint sentinel = (uint)c.Queue[index].V, current = loop.Next;
                int guard = 0;
                if (loop.Open != 0) { complete = false; break; }
                while (current != sentinel)
                {
                    if (guard++ > 100000 || !edges.TryGetValue(current, out var edge) || edge == null || !edge.HasEndpoints
                        || edge.FaceA == edge.FaceB || edge.FaceA != face && edge.FaceB != face)
                    {
                        complete = false;
                        break;
                    }
                    bool firstFace = edge.FaceA == face;
                    var points = edge.AuthoredPoints().Select(point => firstFace ? (point.UA, point.VA) : (point.UB, point.VB)).ToList();
                    curved |= points.Count > 2;
                    coedges.Add(new FaceArea.Coedge(points, edge.Flags, !firstFace));
                    current = firstFace ? edge.NextA : edge.NextB;
                }
                if (!complete) break;
            }
            if (complete && curved && FaceArea.Compute(coedges) is { } area) result[face] = area;
        }
        return result;
    }
}
