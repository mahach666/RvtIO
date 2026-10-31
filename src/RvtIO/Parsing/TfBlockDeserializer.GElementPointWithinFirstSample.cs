namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourcePolygonUvVertex(int EdgeQueueIndex, int PointOrdinal, int FaceSide,
        IReadOnlyList<long> UvBits);
    internal sealed record GElementOtherLoopPointWithinFirstSample(int LoopQueueIndex, bool AfterUnresolvedCheck,
        string Outcome, int? PointWithinState, IReadOnlyList<SourcePolygonUvVertex>? Vertices,
        SourcePolygonCrossings? Polygon);
    internal sealed record GElementLoopPointWithinFirstSample(int LoopQueueIndex, string Outcome,
        bool? ResolvedShouldBeCcw, int? ContainmentCount, IReadOnlyList<GElementOtherLoopPointWithinFirstSample> Checks);
    internal sealed record GElementFacePointWithinFirstSample(int FaceQueueIndex,
        IReadOnlyList<GElementLoopPointWithinFirstSample> Loops);
    internal sealed record OwnGElementFreshPointWithinFirstSample(int OwnerId, int CurrentTessEpsVersion,
        IReadOnlyList<GElementFacePointWithinFirstSample> Faces, bool Complete);

    /// <summary>Vertex iterator for a closed two-point edge chain ending at its own sentinel. Within each edge,
    /// (side == 1) XOR flag 0 chooses last-first instead of first-last. Both points and the inter-edge connectors are
    /// kept; chains do not reorder the base boundary. PointWithin callers must separately qualify the ordinary/no-chain
    /// or the chain-aware predicate; this sequence does not select that algorithm.</summary>
    internal static IReadOnlyList<SourcePolygonUvVertex>? SelectSourceTwoPointPolygonVertices(
        GElementLoopTopology loop, out string? failure)
    {
        failure = "two-point-polygon-vertices";
        if (loop.Open != 0 || loop.EndOutcome != "loop-sentinel" || loop.Edges.Count == 0
            || loop.ConstructorSkippedLoops.Count != 0 || loop.FirstEdgeQueueIndex != loop.InitialEdgeQueueIndex
            || loop.InitialEdgeQueueIndex != loop.Edges[0].EdgeQueueIndex) return null;
        var vertices = new List<SourcePolygonUvVertex>();
        for (int i = 0; i < loop.Edges.Count; i++)
        {
            var edge = loop.Edges[i];
            int expectedNext = i + 1 == loop.Edges.Count ? loop.QueueIndex : loop.Edges[i + 1].EdgeQueueIndex;
            if (edge.NextQueueIndex != expectedNext || edge.PointCount != 2 || (edge.Flags & 6) != 6
                || edge.InteriorPoints.Count != 0 || edge.FirstAndLastPoints.Count != 2
                || edge.FaceSide is not (0 or 1)) return null;
            int side = edge.FaceSide.Value;
            bool reverse = (side == 1) != ((edge.Flags & 1) != 0);
            foreach (int ordinal in reverse ? new[] { 1, 0 } : new[] { 0, 1 })
            {
                var p = edge.FirstAndLastPoints[ordinal]; var bits = side == 0 ? p.Side0Bits : p.Side1Bits;
                if (bits.Count != 2) return null;
                vertices.Add(new(edge.EdgeQueueIndex, ordinal, side, bits));
            }
        }
        failure = null; return vertices;
    }

    /// <summary>First sample containment and parity for an own capture. A qualified projection with all distances > EPS
    /// and polygon true returns state 1. Polygon false stays pending: runtime flags and fallback are not assumed off.
    /// Boundary (2) and unknowns prevent a final parity; later sample candidates are conditional.
    /// No retry, chain or runtime state is claimed.</summary>
    internal static OwnGElementFreshPointWithinFirstSample? SelectOwnGElementPointWithinFirstSample(
        OwnGElementGeometryGraph graph, int currentTessEpsVersion, out string? failure)
    {
        var projections = SelectOwnGElementLoopTwoPointProjections(graph, currentTessEpsVersion, out failure);
        if (projections == null) return null;
        var topology = SelectOwnGElementLoopTopology(graph, out failure);
        if (topology == null) return null;
        var inputs = SelectOwnGElementLoopNestingInputs(graph, currentTessEpsVersion, out failure);
        if (inputs == null) return null;
        var loopsByQ = topology.Faces.SelectMany(f => f.Loops).GroupBy(l => l.QueueIndex).ToDictionary(g => g.Key, g => g.First());
        var samples = inputs.Faces.SelectMany(f => f.Loops).GroupBy(l => l.LoopQueueIndex).ToDictionary(g => g.Key, g => g.First());
        var faces = new List<GElementFacePointWithinFirstSample>(); bool complete = true;
        foreach (var face in projections.Faces)
        {
            var loops = new List<GElementLoopPointWithinFirstSample>();
            foreach (var current in face.Loops)
            {
                var checks = new List<GElementOtherLoopPointWithinFirstSample>(); bool pending = false; int count = 0;
                foreach (var prior in current.Checks)
                {
                    string outcome = prior.Outcome; int? state = prior.PointWithinState;
                    IReadOnlyList<SourcePolygonUvVertex>? vertices = null; SourcePolygonCrossings? polygon = null;
                    if (outcome == "pending-polygon")
                    {
                        vertices = SelectSourceTwoPointPolygonVertices(loopsByQ[prior.LoopQueueIndex], out failure);
                        if (vertices == null) return null;
                        var sample = samples[current.LoopQueueIndex].SampleUvBits;
                        if (sample == null) { failure = "point-within-first-sample-uv"; return null; }
                        polygon = EvaluateSourcePolygonCrossings(vertices.Select(p => p.UvBits).ToArray(), sample, out failure);
                        if (polygon == null) return null;
                        if (polygon.IsInside) { outcome = "polygon-inside"; state = 1; }
                        else { outcome = "polygon-outside-runtime-fallback-pending"; state = null; }
                    }
                    checks.Add(new(prior.LoopQueueIndex, pending, outcome, state, vertices, polygon));
                    if (state == 1) count++;
                    else if (state != 0 && outcome != "skipped-open") pending = true;
                }
                bool? resolved = current.ResolvedShouldBeCcw; string result = current.Outcome; int? containment = null;
                if (current.Outcome == "pending-point-within")
                {
                    resolved = pending ? null : (count & 1) == 0;
                    containment = pending ? null : count;
                    result = pending ? "pending-first-sample-point-within" : "first-sample-parity";
                    if (pending) complete = false;
                }
                loops.Add(new(current.LoopQueueIndex, result, resolved, containment, checks));
            }
            faces.Add(new(face.FaceQueueIndex, loops));
        }
        failure = null; return new(graph.OwnerId, currentTessEpsVersion, faces, complete);
    }
}
