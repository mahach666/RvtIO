namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourceWindingClassification(int Code, int Location);
    internal sealed record GElementPlaneWindingEdge(int EdgeQueueIndex, int FaceSide, bool Reverse, SourceScalarIntegral Integral);
    internal sealed record GElementPlaneWindingFallback(int SourceLoopQueueIndex, int TargetLoopQueueIndex,
        string Outcome, bool? Orientation, IReadOnlyList<GElementPlaneWindingEdge> Edges, long? TotalBits,
        SourceWindingClassification? Classification, int? UnrefinedPointWithinState);
    internal sealed record OwnGElementPlaneWindingFallback(OwnGElementFreshPointWithinFirstSample PriorFirstSample,
        OwnGElementFreshPointWithinFirstSample UnrefinedFirstSample, IReadOnlyList<GElementPlaneWindingFallback> Fallbacks,
        bool ActualRefinementContextClosed);

    /// <summary>Classifier with strict 0.05 windows in the order -1, 0, +1.
    /// Other finite totals return code 1 with location 0.</summary>
    internal static SourceWindingClassification? ClassifySourceWindingTotal(long totalBits, out string? failure)
    {
        failure = "winding-classification";
        double value = BitConverter.Int64BitsToDouble(totalBits); if (!double.IsFinite(value)) return null;
        failure = null;
        if (0.05 > Math.Abs(value - (-1.0))) return new(0, -1);
        if (0.05 > Math.Abs(value)) return new(0, 0);
        if (0.05 > Math.Abs(value - 1.0)) return new(0, 1);
        return new(1, 0);
    }

    /// <summary>No-refinement UV fallback for own Plane/two-point closed loops. Plane slot 20 with a null derivative
    /// output is false, hence sameSingularPoint is false and the iterator creates no synthetic edge. The refined
    /// state stays unknown, not assumed off. Prior results stay intact; UnrefinedFirstSample is conditional on the
    /// supplied source path.</summary>
    internal static OwnGElementPlaneWindingFallback? SelectOwnGElementPlaneWindingFallback(
        OwnGElementGeometryGraph graph, int epsVersion, out string? failure)
    {
        var prior = SelectOwnGElementChainFirstSample(graph, epsVersion, out failure); if (prior == null) return null;
        var topology = SelectOwnGElementLoopTopology(graph, out failure); if (topology == null) return null;
        var inputs = SelectOwnGElementLoopNestingInputs(graph, epsVersion, out failure); if (inputs == null) return null;
        var nodes = graph.Nodes.ToDictionary(n => n.QueueIndex);
        var loops = topology.Faces.SelectMany(f => f.Loops).GroupBy(l => l.QueueIndex).ToDictionary(g => g.Key, g => g.First());
        var samples = inputs.Faces.SelectMany(f => f.Loops).GroupBy(l => l.LoopQueueIndex).ToDictionary(g => g.Key, g => g.First());
        var results = new List<GElementPlaneWindingFallback>(); var faces = new List<GElementFacePointWithinFirstSample>();
        bool complete = true; string context = "plane-winding-fallback";
        try
        {
            foreach (var face in prior.FirstSample.Faces)
            {
                var updated = new List<GElementLoopPointWithinFirstSample>();
                foreach (var current in face.Loops)
                {
                    var checks = new List<GElementOtherLoopPointWithinFirstSample>(); bool pending = false; int count = 0;
                    foreach (var check in current.Checks)
                    {
                        var value = check;
                        if (check.Outcome == "polygon-outside-runtime-fallback-pending")
                        {
                            context = $"plane-winding:{current.LoopQueueIndex}:{check.LoopQueueIndex}";
                            var result = Evaluate(current.LoopQueueIndex, check.LoopQueueIndex); results.Add(result);
                            value = check with { Outcome = result.Outcome, PointWithinState = result.UnrefinedPointWithinState };
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
                            Outcome = pending ? current.Outcome : "unrefined-plane-winding-parity" };
                        if (pending) complete = false;
                    }
                    updated.Add(row);
                }
                faces.Add(new(face.FaceQueueIndex, updated));
            }
            failure = null; return new(prior.FirstSample, prior.FirstSample with { Faces = faces, Complete = complete }, results, false);
        }
        catch (DecodeFail) { failure = context; return null; }

        GElementPlaneWindingFallback Evaluate(int sourceQ, int targetQ)
        {
            var target = loops[targetQ]; var edges = new List<GElementPlaneWindingEdge>(); bool? orientation = null; long? sum = null;
            GElementPlaneWindingFallback Result(string outcome, SourceWindingClassification? classification = null, int? state = null)
                => new(sourceQ, targetQ, outcome, orientation, edges, sum, classification, state);
            if (SelectSourceTwoPointPolygonVertices(target, out _) == null || target.FaceQueueIndex is not int faceQ)
                return Result("unsupported-unrefined-plane-topology");
            if (nodes[targetQ].ClassId == 1257)
            {
                var chains = Field(nodes[targetQ], 1257, "m_chainsWithEnvelopes");
                if (chains.Items.Count != 0) return Result("unsupported-unrefined-plane-chains");
            }
            var surface = Field(nodes[faceQ], 1589, "m_pSurf").Items[0].Pointer;
            if (surface?.QueueIndex is not int sq || surface.ClassId != 522 || !nodes.TryGetValue(sq, out var plane)
                || plane.ClassId != 522 || plane.Handle != surface.Handle) return Result("unsupported-unrefined-surface");
            var flags = Scalar(nodes[faceQ], 1590, "m_faceFlags_v9"); var orient = Scalar(plane, 523, "m_orientFlag");
            if (flags < 0 || flags > uint.MaxValue || orient < 0 || orient > byte.MaxValue) throw Fail;
            orientation = ((flags >> 1) & 1) != orient;
            var query = samples[sourceQ].SampleUvBits; if (query == null) throw Fail;
            double total = 0;
            foreach (var edge in target.Edges)
            {
                int side = edge.FaceSide!.Value; bool reverse = (side == 1) != ((edge.Flags & 1) != 0);
                var first = side == 0 ? edge.FirstAndLastPoints[0].Side0Bits : edge.FirstAndLastPoints[0].Side1Bits;
                var last = side == 0 ? edge.FirstAndLastPoints[1].Side0Bits : edge.FirstAndLastPoints[1].Side1Bits;
                var integral = IntegrateSourceWindingSegment(first, last, query, orientation.Value, reverse, 1e-6, out _);
                if (integral == null) return Result("unsupported-unrefined-integral");
                edges.Add(new(edge.EdgeQueueIndex, side, reverse, integral));
                if (integral.Code != 0) return Result("unrefined-integral-error-boundary", null, 2);
                total += BitConverter.Int64BitsToDouble(integral.ValueBits!.Value);
                if (!double.IsFinite(total)) return Result("unsupported-unrefined-sum");
            }
            sum = BitConverter.DoubleToInt64Bits(total);
            var classified = ClassifySourceWindingTotal(sum.Value, out _); if (classified == null) throw Fail;
            if (classified.Code != 0) return Result("unrefined-classification-error-boundary", classified, 2);
            return classified.Location == 0 ? Result("unrefined-plane-winding-outside", classified, 0)
                : Result("unrefined-plane-winding-inside", classified, 1);
        }
        static GElementGraphField Field(GElementGraphNode node, int declaring, string name)
        { var fs = node.Fields.Where(f => f.Declaring == declaring && f.Name == name).ToArray(); if (fs.Length != 1) throw Fail; return fs[0]; }
        static long Scalar(GElementGraphNode node, int declaring, string name)
        { var f = Field(node, declaring, name); if (f.Items.Count != 1 || f.Items[0].Values.Count != 1) throw Fail; return f.Items[0].Values[0]; }
    }
}
