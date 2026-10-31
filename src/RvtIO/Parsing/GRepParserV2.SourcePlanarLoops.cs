using RvtIO.Internal;

namespace RvtIO.Parsing;

internal static partial class GRepParserV2
{
    // Same-frame source193//224 contracts; no current-body selection here.
    internal static Dictionary<int, List<GRepFace>>? SelectOwnPlanarFaceRegions(
        TfBlockDeserializer.OwnGElementGeometryGraph graph, out string? failure)
    {
        var topology = TfBlockDeserializer.SelectOwnGElementLoopTopology(graph, out failure);
        if (topology == null) return null;
        var winding = TfBlockDeserializer.SelectOwnGElementEpsIndependentSamples(graph, out failure);
        if (winding == null) return null;
        var nodes = graph.Nodes.ToDictionary(n => n.QueueIndex);
        var decisions = winding.Loops.ToDictionary(l => (l.FaceQueueIndex, l.LoopQueueIndex));
        var result = new Dictionary<int, List<GRepFace>>();
        foreach (var face in topology.Faces)
        {
            if (face.Loops.Count == 0) continue;
            failure = $"source-planar-surface:{face.FaceQueueIndex}";
            var links = graph.FaceSurfaces.Where(l => l.FaceQueueIndex == face.FaceQueueIndex).ToArray();
            if (links.Length != 1 || !nodes.TryGetValue(links[0].SurfaceQueueIndex, out var plane)
                || plane.ClassId != 522) return null;
            var rings = new List<List<double[]>>();
            var outerByRing = new Dictionary<List<double[]>, bool>();
            foreach (var loop in face.Loops)
            {
                failure = $"source-planar-loop:{face.FaceQueueIndex}:{loop.QueueIndex}";
                if (loop.FaceQueueIndex != face.FaceQueueIndex
                    || loop.ClassId is not (1254 or 1257) || !loop.GetOuterLoopsCandidate
                    || !decisions.TryGetValue((face.FaceQueueIndex, loop.QueueIndex), out var decision)
                    || decision.IndependentShouldBeCcw is not bool outer) return null;
                var vertices = TfBlockDeserializer.SelectSourceTwoPointPolygonVertices(loop, out string? why);
                if (vertices == null) { failure += ":" + why; return null; }
                var ring = new List<double[]>();
                foreach (var vertex in vertices)
                {
                    double[] uv = [BitConverter.Int64BitsToDouble(vertex.UvBits[0]),
                        BitConverter.Int64BitsToDouble(vertex.UvBits[1]), 0];
                    if (uv.Any(v => !double.IsFinite(v))) return null;
                    if (ring.Count == 0 || !Same(ring[^1], uv)) ring.Add(uv);
                }
                if (ring.Count > 1 && Same(ring[0], ring[^1])) ring.RemoveAt(ring.Count - 1);
                if (ring.Count < 3) return null;
                rings.Add(ring); outerByRing.Add(ring, outer);
            }
            failure = $"source-planar-region-parity:{face.FaceQueueIndex}";
            var regions = ClassifyCoplanarRings(rings, exactPredicates: true);
            if (regions.Count + regions.Sum(r => r.Holes.Count) != rings.Count
                || regions.Any(r => !outerByRing[r.Outer] || r.Holes.Any(h => outerByRing[h]))) return null;
            var faces = new List<GRepFace>();
            var sourceLoops = rings.Select(WorldRing).ToList();
            if (sourceLoops.Any(loop => loop == null)) return null;
            foreach (var region in regions)
            {
                var world = WorldRing(region.Outer);
                var holes = region.Holes.Select(WorldRing).ToList();
                if (world == null || holes.Any(h => h == null)) return null;
                var shape = NewellNormalArea(world);
                if (!double.IsFinite(shape.Area) || shape.Area <= 0 || shape.Normal.Any(v => !double.IsFinite(v))) return null;
                faces.Add(new GRepFace { Vertices = world, Origin = world[0], XVector = shape.Normal,
                    FirstSourceLoop = sourceLoops[0], SourceLoops = sourceLoops!,
                    InnerVertices = holes.Count == 0 ? null : holes.Select(h => h!).ToList() });
            }
            result.Add(face.FaceQueueIndex, faces);

            List<double[]>? WorldRing(List<double[]> ring)
            {
                var world = new List<double[]>();
                foreach (var uv in ring)
                {
                    var bits = TfBlockDeserializer.EvaluateSourceOwnPlanePoint(plane,
                        [BitConverter.DoubleToInt64Bits(uv[0]), BitConverter.DoubleToInt64Bits(uv[1])], out _);
                    if (bits == null) return null;
                    world.Add(bits.Select(BitConverter.Int64BitsToDouble).ToArray());
                }
                return world;
            }
        }
        failure = null;
        return result;

        static bool Same(double[] a, double[] b) => a[0] == b[0] && a[1] == b[1];
    }
}
