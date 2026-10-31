namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourceEnvelopeContainment(bool IsInside, string Outcome,
        IReadOnlyList<long>? ClosestBits, IReadOnlyList<long>? GrowthBits, IReadOnlyList<long>? DeltaBits, long? DistanceBits);
    internal sealed record GElementOtherLoopEnvelopeCheck(int LoopQueueIndex, bool AfterUnresolvedCheck, string Outcome, int? PointWithinState,
        IReadOnlyList<long>? EnvelopeBits, GElementSurfaceFirstForm? SurfaceMetric, SourceEnvelopeContainment? EnvelopeResult);
    internal sealed record GElementLoopEnvelopeFirstPass(int LoopQueueIndex, string Outcome, bool? ResolvedShouldBeCcw,
        IReadOnlyList<GElementOtherLoopEnvelopeCheck> Checks);
    internal sealed record GElementFaceLoopEnvelopeFirstPass(int FaceQueueIndex, IReadOnlyList<GElementLoopEnvelopeFirstPass> Loops);
    internal sealed record OwnGElementFreshLoopEnvelopeFirstPass(int OwnerId, int CurrentTessEpsVersion,
        IReadOnlyList<GElementFaceLoopEnvelopeFirstPass> Faces, bool Complete);

    /// <summary>Envelope.pntInside with finite supplied coordinates and EPS, an optional first-form matrix, 1e-9 margins and
    /// scaled absolute-component vecLength2d. Envelope acceptance is not contour containment. Unsupported non-finite
    /// inputs and reportMathProblem paths remain explicit failures; an invalid finite envelope is the warning/false result.</summary>
    internal static SourceEnvelopeContainment? EvaluateSourceEnvelopeContainment(IReadOnlyList<long> cornersBits,
        IReadOnlyList<long> pointBits, long epsilonBits, IReadOnlyList<long>? matrixBits, out string? failure)
    {
        failure = "envelope-input-shape";
        if (cornersBits.Count != 4 || pointBits.Count != 2 || matrixBits != null && matrixBits.Count != 4) return null;
        double[] c = cornersBits.Select(BitConverter.Int64BitsToDouble).ToArray();
        double[] p = pointBits.Select(BitConverter.Int64BitsToDouble).ToArray();
        double eps = BitConverter.Int64BitsToDouble(epsilonBits);
        failure = "envelope-nonfinite-input";
        if (c.Any(v => !double.IsFinite(v)) || p.Any(v => !double.IsFinite(v)) || !double.IsFinite(eps)) return null;
        if (c[2] < c[0] || c[3] < c[1])
        { failure = null; return new(false, "invalid-envelope", null, null, null, null); }
        double tiny = BitConverter.Int64BitsToDouble(0x3e112e0be826d695); // original PE1e-9
        double[] closest = new double[2]; bool inside = true;
        for (int i = 0; i < 2; i++)
        {
            if (c[i] - tiny > p[i]) { closest[i] = c[i]; inside = false; }
            else if (p[i] > c[i + 2] + tiny) { closest[i] = c[i + 2]; inside = false; }
            else closest[i] = p[i];
        }
        long[] closestBits = closest.Select(BitConverter.DoubleToInt64Bits).ToArray();
        if (inside) { failure = null; return new(true, "inside-margin", closestBits, null, null, null); }
        double[]? m = matrixBits?.Select(BitConverter.Int64BitsToDouble).ToArray();
        failure = "envelope-nonfinite-metric";
        if (m != null && m.Any(v => !double.IsFinite(v))) return null;
        double[] growth = m == null ? [eps, eps] : [((m[0] * 0.5) + 0.5) * eps, ((m[3] * 0.5) + 0.5) * eps];
        growth[0] += tiny; growth[1] += tiny;
        long[] growthBits = growth.Select(BitConverter.DoubleToInt64Bits).ToArray();
        for (int i = 0; i < 2; i++)
            if (c[i] - growth[i] > p[i] || p[i] > c[i + 2] + growth[i])
            { failure = null; return new(false, "axis-reject", closestBits, growthBits, null, null); }
        double[] delta = [p[0] - closest[0], p[1] - closest[1]];
        failure = "envelope-distance-math-problem";
        if (delta.Any(v => !double.IsFinite(v))) return null;
        double u = Math.Abs(delta[0]), v = Math.Abs(delta[1]), scale, x, y, distance;
        if (v > u) { scale = v; x = u / v; y = 1.0; }
        else if (u == 0) { scale = 0; x = y = 0; }
        else { scale = u; x = 1.0; y = v / u; }
        if (scale == 0) distance = 0;
        else
        {
            // Absolute scaled components are used, even with cross terms.
            // The four products and additions are rounded separately.
            double q = m == null ? y * y + x * x :
                (((x * m[1] * y) + (x * m[0] * x)) + (y * m[2] * x)) + (y * m[3] * y);
            if (!double.IsFinite(q) || q < -tiny) return null; // runtime reportMathProblem policy not closed
            distance = (q < 0 ? 0 : Math.Sqrt(q)) * scale;
        }
        failure = null;
        bool accepted = !(eps < distance);
        return new(accepted, accepted ? "within-distance" : "distance-reject", closestBits, growthBits,
            delta.Select(BitConverter.DoubleToInt64Bits).ToArray(), BitConverter.DoubleToInt64Bits(distance));
    }

    /// <summary>First sample pass over the other own Face loops. Closed empty or no-Face pntWithin paths return 0;
    /// unset envelopes stay pending; rejecting set envelopes return 0 with null optional outputs. If all visited loops
    /// return 0 or are skipped open, the parity count 0 resolves outer = true. Accepted envelopes stay unknown, not
    /// inside/boundary guesses. Checks after an unresolved one are first-sample candidates, not a validated execution
    /// trace. Boundary retries, projection/polygon and the runtime state are not closed.</summary>
    internal static OwnGElementFreshLoopEnvelopeFirstPass? SelectOwnGElementLoopEnvelopeFirstPass(
        OwnGElementGeometryGraph graph, int currentTessEpsVersion, out string? failure)
    {
        var nesting = SelectOwnGElementLoopNestingInputs(graph, currentTessEpsVersion, out failure);
        if (nesting == null) return null;
        var topo = SelectOwnGElementLoopTopology(graph, out failure);
        if (topo == null) return null;
        var nodes = graph.Nodes.ToDictionary(n => n.QueueIndex);
        var faces = topo.Faces.ToDictionary(f => f.FaceQueueIndex);
        var result = new List<GElementFaceLoopEnvelopeFirstPass>(); bool complete = true;
        string context = "loop-envelope-first-pass";
        try
        {
            foreach (var face in nesting.Faces)
            {
                var loops = new List<GElementLoopEnvelopeFirstPass>();
                foreach (var current in face.Loops)
                {
                    var checks = new List<GElementOtherLoopEnvelopeCheck>();
                    bool? resolved = current.EarlyShouldBeCcw; string outcome = current.Outcome;
                    if (outcome == "pending-containment")
                    {
                        context = $"loop-envelope-source:{face.FaceQueueIndex}:{current.LoopQueueIndex}";
                        if (current.FaceQueueIndex is not int q || !faces.TryGetValue(q, out var ownFace)
                            || current.SampleUvBits == null || current.Epsilon == null) throw Fail;
                        bool pending = false;
                        foreach (var other in ownFace.Loops)
                        {
                            if (other.QueueIndex == current.LoopQueueIndex) continue;
                            bool afterUnresolved = pending;
                            context = $"loop-envelope-target:{current.LoopQueueIndex}:{other.QueueIndex}";
                            string state; int? within; IReadOnlyList<long>? envelope = null;
                            GElementSurfaceFirstForm? metric = null; SourceEnvelopeContainment? check = null;
                            if (other.Open != 0) { state = "skipped-open"; within = null; }
                            else if (other.FirstEdgeQueueIndex == other.QueueIndex) { state = "empty-outside"; within = 0; }
                            else if (other.FaceQueueIndex == null) { state = "no-face-outside"; within = 0; }
                            else
                            {
                                var target = nodes[other.QueueIndex];
                                var field = Field(target, 1255, "m_Envelope", 1);
                                var item = field.Items[0];
                                if (item.Fields.Count != 1 || item.Pointer != null || item.Values.Count != 0 || item.End - item.Start != 32) throw Fail;
                                var corners = item.Fields[0];
                                if (corners.Declaring != 524 || corners.Name != "m_corners" || corners.Count != 2 || corners.Items.Count != 2) throw Fail;
                                foreach (var p in corners.Items)
                                    if (p.Values.Count != 2 || p.End - p.Start != 16 || p.Pointer != null || p.Fields.Count != 0) throw Fail;
                                envelope = corners.Items.SelectMany(p => p.Values).ToArray();
                                double[] c = envelope.Select(BitConverter.Int64BitsToDouble).ToArray();
                                if (c.Any(v => !double.IsFinite(v))) throw Fail;
                                bool isSet = c[2] >= c[0] && c[3] >= c[1];
                                if (!isSet) { state = "unset-envelope-pending"; within = null; pending = true; }
                                else
                                {
                                    var ownerFace = nodes[other.FaceQueueIndex.Value];
                                    var surface = Field(ownerFace, 1589, "m_pSurf", 1).Items[0].Pointer;
                                    if (surface == null || surface.HandToken != null) throw Fail;
                                    if (surface.Handle == 0)
                                    { if (surface.ClassId != null || surface.QueueIndex != null) throw Fail; }
                                    else
                                    {
                                        if (surface.ClassId is not (522 or 983) || surface.QueueIndex is not int sq
                                            || !nodes.TryGetValue(sq, out var sn) || sn.ClassId != surface.ClassId || sn.Handle != surface.Handle) throw Fail;
                                        metric = ComputeOwnGElementSurfaceFirstForm(sn, current.SampleUvBits, out _);
                                        if (metric == null) throw Fail;
                                    }
                                    check = EvaluateSourceEnvelopeContainment(envelope, current.SampleUvBits,
                                        current.Epsilon.VertexEpsilonBits, metric?.MatrixBits, out _);
                                    if (check == null) throw Fail;
                                    if (check.IsInside) { state = "accepted-envelope-pending"; within = null; pending = true; }
                                    else { state = "rejected-envelope-outside"; within = 0; }
                                }
                            }
                            checks.Add(new(other.QueueIndex, afterUnresolved, state, within, envelope, metric, check));
                        }
                        resolved = pending ? null : true;
                        outcome = pending ? "pending-point-within" : "all-other-loops-outside";
                        if (pending) complete = false;
                    }
                    loops.Add(new(current.LoopQueueIndex, outcome, resolved, checks));
                }
                result.Add(new(face.FaceQueueIndex, loops));
            }
            failure = null; return new(graph.OwnerId, currentTessEpsVersion, result, complete);
        }
        catch (DecodeFail) { failure = context; return null; }

        static GElementGraphField Field(GElementGraphNode node, int declaring, string name, int count)
        {
            var fields = node.Fields.Where(f => f.Declaring == declaring && f.Name == name).ToArray();
            if (fields.Length != 1 || fields[0].Items.Count != count) throw Fail;
            return fields[0];
        }
    }
}
