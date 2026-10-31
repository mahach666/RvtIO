namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourceChainRange(int StartVisit, int EndVisit, IReadOnlyList<long> EnvelopeBits);
    internal sealed record SourceChainRaySegment(SourcePolygonUvVertex From, SourcePolygonUvVertex To,
        long? IntersectionXBits, bool Crossed);
    internal sealed record SourceChainRayVisit(int ChainIndex, bool Skipped, bool StartsWithPreviousEndpoint,
        IReadOnlyList<SourceChainRaySegment> Segments);
    internal sealed record SourceChainRay(int Crossings, bool IsInside, IReadOnlyList<SourceChainRayVisit> Chains);

    /// <summary>Closed reciprocal two-point chain partition: envelope skip with the adjacent-Y override, previous
    /// endpoint after a skipped chain, next-start connector at every edge, and left-ray parity. This is not an
    /// independent polygon closure of each chain or of the concatenated ray.</summary>
    internal static SourceChainRay? EvaluateSourceTwoPointChainRay(GElementLoopTopology loop,
        IReadOnlyList<SourceChainRange> ranges, IReadOnlyList<long> queryBits, out string? failure)
    {
        failure = "two-point-chain-ray";
        if (ranges.Count == 0 || queryBits.Count != 2 || loop.Edges.Count == 0) return null;
        // This admission validates the vertex payload, next chain and sentinel; the
        // caller separately confirms the reciprocal previous links from the archive keys.
        if (SelectSourceTwoPointPolygonVertices(loop, out _) == null) return null;
        double qx = BitConverter.Int64BitsToDouble(queryBits[0]), qy = BitConverter.Int64BitsToDouble(queryBits[1]);
        if (!double.IsFinite(qx) || !double.IsFinite(qy)) return null;
        var envelopes = new List<double[]>(); int end = 0;
        foreach (var range in ranges)
        {
            if (range.StartVisit != end || range.EndVisit <= end || range.EndVisit > loop.Edges.Count || range.EnvelopeBits.Count != 4) return null;
            var e = range.EnvelopeBits.Select(BitConverter.Int64BitsToDouble).ToArray();
            if (e.Any(v => !double.IsFinite(v)) || e[2] < e[0] || e[3] < e[1]) return null;
            envelopes.Add(e); end = range.EndVisit;
        }
        if (end != loop.Edges.Count) return null;
        double margin = BitConverter.Int64BitsToDouble(0x3e112e0be826d695);
        double below = qy - margin, above = qy + margin, right = qx + margin;
        if (!double.IsFinite(below) || !double.IsFinite(above) || !double.IsFinite(right)) return null;
        var visits = new List<SourceChainRayVisit>(); int? skipped = null; bool startsNormally = true; int crossings = 0;
        for (int i = 0; i < ranges.Count; i++)
        {
            var e = envelopes[i]; var range = ranges[i];
            bool skip = below > e[3] || e[1] > above || e[0] > right;
            if (skip && skipped is int previous && OppositeY(e, envelopes[previous])) skip = false;
            if (skip && i + 1 == ranges.Count && OppositeY(e, envelopes[0])) skip = false;
            if (skip)
            {
                visits.Add(new(i, true, false, [])); skipped = i; startsNormally = false; continue;
            }
            bool previousEndpoint = !startsNormally; var segments = new List<SourceChainRaySegment>();
            var from = Vertex(previousEndpoint ? (range.StartVisit + loop.Edges.Count - 1) % loop.Edges.Count : range.StartVisit,
                previousEndpoint);
            if (previousEndpoint && !Add(Vertex(range.StartVisit, false))) return null;
            for (int j = range.StartVisit; j < range.EndVisit; j++)
            {
                if (!Add(Vertex(j, true)) || !Add(Vertex((j + 1) % loop.Edges.Count, false))) return null;
            }
            visits.Add(new(i, false, previousEndpoint, segments)); skipped = null; startsNormally = true;

            bool Add(SourcePolygonUvVertex to)
            {
                double ax = BitConverter.Int64BitsToDouble(from.UvBits[0]), ay = BitConverter.Int64BitsToDouble(from.UvBits[1]);
                double bx = BitConverter.Int64BitsToDouble(to.UvBits[0]), by = BitConverter.Int64BitsToDouble(to.UvBits[1]);
                if (!double.IsFinite(ax) || !double.IsFinite(ay) || !double.IsFinite(bx) || !double.IsFinite(by)) return false;
                long? intersection = null; bool crossed = false;
                if ((ay >= qy) == (qy > by))
                {
                    double x = ((ax - bx) * (qy - ay)) / (ay - by) + ax;
                    if (!double.IsFinite(x)) return false;
                    intersection = BitConverter.DoubleToInt64Bits(x); crossed = qx > x;
                    if (crossed) crossings++;
                }
                segments.Add(new(from, to, intersection, crossed)); from = to; return true;
            }
        }
        failure = null; return new(crossings, (crossings & 1) != 0, visits);

        bool OppositeY(double[] current, double[] previous)
            => below > current[3] && previous[1] >= above || current[1] > above && below >= previous[3];
        SourcePolygonUvVertex Vertex(int visit, bool last)
        {
            var edge = loop.Edges[visit]; int side = edge.FaceSide!.Value;
            bool reverse = (side == 1) != ((edge.Flags & 1) != 0); int ordinal = last != reverse ? 1 : 0;
            var p = edge.FirstAndLastPoints[ordinal];
            return new(edge.EdgeQueueIndex, ordinal, side, side == 0 ? p.Side0Bits : p.Side1Bits);
        }
    }
}
