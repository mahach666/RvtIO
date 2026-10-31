namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record GElementChainProjection(int ChainIndex, SourceEnvelopeContainment Envelope,
        IReadOnlyList<GElementProjectedEdge> Edges);
    internal sealed record GElementChainFirstSampleCheck(int SourceLoopQueueIndex, int TargetLoopQueueIndex,
        string Outcome, int? PointWithinState, IReadOnlyList<SourceChainRange> Ranges,
        IReadOnlyList<GElementChainProjection> Projections, SourceChainRay? Ray);
    internal sealed record OwnGElementFreshChainFirstSample(OwnGElementFreshPointWithinFirstSample FirstSample,
        IReadOnlyList<GElementChainFirstSampleCheck> ChainChecks);

    internal static OwnGElementFreshChainFirstSample? SelectOwnGElementChainFirstSample(
        OwnGElementGeometryGraph graph, int epsVersion, out string? failure)
    {
        var prior = SelectOwnGElementPointWithinFirstSample(graph, epsVersion, out failure); if (prior == null) return null;
        var topo = SelectOwnGElementLoopTopology(graph, out failure); if (topo == null) return null;
        var inputs = SelectOwnGElementLoopNestingInputs(graph, epsVersion, out failure); if (inputs == null) return null;
        var refs = SelectOwnGElementFreshArchiveReferences(graph, out failure); if (refs == null || !refs.Complete) return null;
        var nodes = graph.Nodes.ToDictionary(n => n.QueueIndex);
        var loops = topo.Faces.SelectMany(f => f.Loops).GroupBy(l => l.QueueIndex).ToDictionary(g => g.Key, g => g.First());
        var samples = inputs.Faces.SelectMany(f => f.Loops).GroupBy(l => l.LoopQueueIndex).ToDictionary(g => g.Key, g => g.First());
        var links = refs.References.ToDictionary(r => (r.SourceQueueIndex, r.Declaring, r.Name, r.ItemIndex, r.Start));
        var results = new List<GElementChainFirstSampleCheck>(); var faces = new List<GElementFacePointWithinFirstSample>();
        bool complete = true; string context = "chain-first-sample";
        try
        {
            foreach (var face in prior.Faces)
            {
                var updated = new List<GElementLoopPointWithinFirstSample>();
                foreach (var current in face.Loops)
                {
                    var checks = new List<GElementOtherLoopPointWithinFirstSample>(); bool pending = false; int count = 0;
                    foreach (var check in current.Checks)
                    {
                        var value = check;
                        if (check.Outcome == "unsupported-projection-topology" && nodes[check.LoopQueueIndex].ClassId == 1257)
                        {
                            context = $"chain-first-sample:{current.LoopQueueIndex}:{check.LoopQueueIndex}";
                            var result = Evaluate(current.LoopQueueIndex, check.LoopQueueIndex); results.Add(result);
                            value = check with { Outcome = result.Outcome, PointWithinState = result.PointWithinState };
                        }
                        value = value with { AfterUnresolvedCheck = pending }; checks.Add(value);
                        if (value.PointWithinState == 1) count++;
                        else if (value.PointWithinState != 0 && value.Outcome != "skipped-open") pending = true;
                    }
                    var row = current with { Checks = checks };
                    if (current.Outcome == "pending-first-sample-point-within")
                    {
                        row = row with { ResolvedShouldBeCcw = pending ? null : (count & 1) == 0,
                            ContainmentCount = pending ? null : count,
                            Outcome = pending ? current.Outcome : "chain-first-sample-parity" };
                        if (pending) complete = false;
                    }
                    updated.Add(row);
                }
                faces.Add(new(face.FaceQueueIndex, updated));
            }
            failure = null; return new(prior with { Faces = faces, Complete = complete }, results);
        }
        catch (DecodeFail) { failure = context; return null; }

        GElementChainFirstSampleCheck Evaluate(int sourceQ, int targetQ)
        {
            var target = loops[targetQ]; var node = nodes[targetQ]; var sample = samples[sourceQ];
            var ranges = new List<SourceChainRange>(); var projected = new List<GElementChainProjection>();
            GElementChainFirstSampleCheck Result(string outcome, int? state = null, SourceChainRay? ray = null)
                => new(sourceQ, targetQ, outcome, state, ranges, projected, ray);
            if (target.FaceQueueIndex is not int faceQ || sample.SampleUvBits == null || sample.Epsilon == null) throw Fail;
            if (SelectSourceTwoPointPolygonVertices(target, out _) == null) return Result("unsupported-chain-topology");
            var chainField = Field(node, 1257, "m_chainsWithEnvelopes");
            if (chainField.Count != chainField.Items.Count || chainField.Items.Count == 0) return Result("unsupported-chain-topology");
            var indices = new List<int>(); var envelopes = new List<IReadOnlyList<long>>();
            foreach (var item in chainField.Items)
            {
                var start = Inline(item, 1253, "m_pStartEdge");
                int? q = Weak(targetQ, start, 0); if (q == null) return Result("unsupported-chain-topology");
                int index = -1;
                for (int i = 0; i < target.Edges.Count; i++) if (target.Edges[i].EdgeQueueIndex == q) { index = i; break; }
                if (index < 0 || indices.Count == 0 && index != 0 || indices.Count != 0 && index <= indices[^1]) return Result("unsupported-chain-topology");
                indices.Add(index);
                var envelope = Inline(item, 1253, "m_envelope");
                if (envelope.Items.Count != 1 || envelope.Items[0].Fields.Count != 1) throw Fail;
                var corners = envelope.Items[0].Fields[0];
                if (corners.Declaring != 524 || corners.Name != "m_corners" || corners.Count != 2 || corners.Items.Count != 2
                    || corners.Items.Any(p => p.Values.Count != 2)) throw Fail;
                var bits = corners.Items.SelectMany(p => p.Values).ToArray(); var values = bits.Select(BitConverter.Int64BitsToDouble).ToArray();
                if (values.Any(v => !double.IsFinite(v)) || values[2] < values[0] || values[3] < values[1]) return Result("unsupported-chain-envelope");
                envelopes.Add(bits);
            }
            if (target.LastEdgeQueueIndex != target.Edges[^1].EdgeQueueIndex) return Result("unsupported-chain-topology");
            for (int i = 0; i < target.Edges.Count; i++)
            {
                var e = target.Edges[i]; int previous = i == 0 ? targetQ : target.Edges[i - 1].EdgeQueueIndex;
                if (Weak(e.EdgeQueueIndex, Field(nodes[e.EdgeQueueIndex], 1244, "m_prev"), e.FaceSide!.Value) != previous)
                    return Result("unsupported-chain-topology");
            }
            for (int i = 0; i < indices.Count; i++) ranges.Add(new(indices[i], i + 1 == indices.Count ? target.Edges.Count : indices[i + 1], envelopes[i]));
            var plane = Plane(faceQ); if (plane == null) return Result("unsupported-chain-query-surface");
            var metric = ComputeOwnGElementSurfaceFirstForm(plane, sample.SampleUvBits, out _); if (metric == null) throw Fail;
            var query = EvaluateSourceOwnPlanePoint(plane, sample.SampleUvBits, out _); if (query == null) throw Fail;
            for (int i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i]; var env = EvaluateSourceEnvelopeContainment(range.EnvelopeBits, sample.SampleUvBits,
                    sample.Epsilon.VertexEpsilonBits, metric.MatrixBits, out _); if (env == null) throw Fail;
                var edges = new List<GElementProjectedEdge>(); projected.Add(new(i, env, edges));
                if (!env.IsInside) continue;
                for (int j = range.StartVisit; j < range.EndVisit; j++)
                {
                    var e = target.Edges[j]; var fn = Field(nodes[e.EdgeQueueIndex], 1244, "m_pFace");
                    int? a = Weak(e.EdgeQueueIndex, fn, 0), b = Weak(e.EdgeQueueIndex, fn, 1), chosen = a ?? b;
                    int side = a == null ? 1 : 0;
                    var endpointPlane = chosen is int q ? Plane(q) : null;
                    if (endpointPlane == null) return Result("unsupported-chain-endpoint-surface");
                    var first = side == 0 ? e.FirstAndLastPoints[0].Side0Bits : e.FirstAndLastPoints[0].Side1Bits;
                    var last = side == 0 ? e.FirstAndLastPoints[1].Side0Bits : e.FirstAndLastPoints[1].Side1Bits;
                    long[] Uv(double t) => Enumerable.Range(0, 2).Select(k => BitConverter.DoubleToInt64Bits(
                        (1.0 - t) * BitConverter.Int64BitsToDouble(first[k]) + t * BitConverter.Int64BitsToDouble(last[k]))).ToArray();
                    var uv0 = Uv(0); var uv1 = Uv(1);
                    var x = EvaluateSourceOwnPlanePoint(endpointPlane, uv0, out _); var z = EvaluateSourceOwnPlanePoint(endpointPlane, uv1, out _);
                    if (x == null || z == null) throw Fail;
                    var projection = ProjectSourceTwoPointSegment(x, z, query, out _); if (projection == null) throw Fail;
                    bool boundary = BitConverter.Int64BitsToDouble(sample.Epsilon.VertexEpsilonBits) >= BitConverter.Int64BitsToDouble(projection.DistanceBits);
                    edges.Add(new(e.EdgeQueueIndex, chosen, boundary ? "boundary" : "projected", uv0, uv1, x, z, projection));
                    if (boundary) return Result("chain-boundary-first-sample", 2);
                }
            }
            var ray = EvaluateSourceTwoPointChainRay(target, ranges, sample.SampleUvBits, out _);
            if (ray == null) return Result("unsupported-chain-ray");
            return ray.IsInside ? Result("chain-polygon-inside", 1, ray) : Result("chain-polygon-outside-runtime-fallback-pending", null, ray);
        }
        GElementGraphNode? Plane(int faceQ)
        {
            var p = Field(nodes[faceQ], 1589, "m_pSurf").Items[0].Pointer;
            if (p == null || p.HandToken != null || p.Handle == 0 || p.QueueIndex is not int q || !nodes.TryGetValue(q, out var n)
                || n.ClassId != p.ClassId || n.Handle != p.Handle) return null;
            return n.ClassId == 522 ? n : null;
        }
        int? Weak(int sourceQ, GElementGraphField f, int index)
        {
            if (index >= f.Items.Count || !links.TryGetValue((sourceQ, f.Declaring, f.Name, index, f.Items[index].Start), out var r)) throw Fail;
            if (r.Outcome == "null") return null;
            if (r.Outcome != "own-node" || r.TargetQueueIndex is not int q) throw Fail; return q;
        }
        static GElementGraphField Field(GElementGraphNode n, int decl, string name)
        { var fs = n.Fields.Where(f => f.Declaring == decl && f.Name == name).ToArray(); if (fs.Length != 1) throw Fail; return fs[0]; }
        static GElementGraphField Inline(GElementGraphItem i, int decl, string name)
        { var fs = i.Fields.Where(f => f.Declaring == decl && f.Name == name).ToArray(); if (fs.Length != 1) throw Fail; return fs[0]; }
    }
}
