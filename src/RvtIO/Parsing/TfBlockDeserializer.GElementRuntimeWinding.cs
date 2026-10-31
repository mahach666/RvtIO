namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record SourceWindingRuntimeMode(bool Bcbc00Nonzero, bool Bcbc01Nonzero, string Path, int? PointWithinState);
    internal sealed record GElementRefinedWindingEdge(int EdgeQueueIndex, int FaceSide, SourceRefinedWindingIntegral Refined);
    internal sealed record GElementRuntimeWindingCheck(int SourceLoopQueueIndex, int TargetLoopQueueIndex,
        string Outcome, IReadOnlyList<GElementRefinedWindingEdge> Edges, long? RefinedTotalBits,
        SourceWindingClassification? RefinedClassification, IReadOnlyList<SourceWindingRuntimeMode> Modes, int? IndependentPointWithinState);
    internal sealed record OwnGElementRuntimeWinding(OwnGElementPlaneWindingFallback Prior,
        OwnGElementFreshPointWithinFirstSample FirstSample, IReadOnlyList<GElementRuntimeWindingCheck> Fallbacks,
        bool RefinementDispatchIndependentForVisitedFallbacks, bool ActualEpsilonAuthorityClosed, bool CurrentGeometryAuthorityClosed);

    /// <summary>Exhausts all zero/non-zero branches of the two runtime flags. Both zero returns outside (0); other
    /// paths use their separately computed states. A divergence, boundary (2) or unknown is not a resolved first-sample state.</summary>
    internal static (IReadOnlyList<SourceWindingRuntimeMode> Modes, int? IndependentState)
        CompareSourceWindingRuntimeModes(int? unrefined, int? refined)
    {
        SourceWindingRuntimeMode[] modes = [new(false, false, "direct-outside", 0),
            new(false, true, "refined", refined), new(true, false, "unrefined", unrefined), new(true, true, "refined", refined)];
        return (modes, modes.All(m => m.PointWithinState == 0) ? 0 : null);
    }

    /// <summary>Independence of the refinement dispatch of the first sample for an own fresh graph with Plane/two-point
    /// edges. Prior results stay intact. The supplied EPS context and the model state are not closed; no default
    /// flag values are inferred.</summary>
    internal static OwnGElementRuntimeWinding? SelectOwnGElementRuntimeWinding(
        OwnGElementGeometryGraph graph, int epsVersion, out string? failure)
    {
        var prior = SelectOwnGElementPlaneWindingFallback(graph, epsVersion, out failure); if (prior == null) return null;
        var topology = SelectOwnGElementLoopTopology(graph, out failure); if (topology == null) return null;
        var inputs = SelectOwnGElementLoopNestingInputs(graph, epsVersion, out failure); if (inputs == null) return null;
        var refs = SelectOwnGElementFreshArchiveReferences(graph, out failure); if (refs == null) return null;
        if (!refs.Complete) { failure = "runtime-winding-archive-incomplete"; return null; }
        var nodes = graph.Nodes.ToDictionary(n => n.QueueIndex);
        var loops = topology.Faces.SelectMany(f => f.Loops).GroupBy(l => l.QueueIndex).ToDictionary(g => g.Key, g => g.First());
        var samples = inputs.Faces.SelectMany(f => f.Loops).GroupBy(l => l.LoopQueueIndex).ToDictionary(g => g.Key, g => g.First());
        var faceLinks = refs.References.Where(r => r.Declaring == 1244 && r.Name == "m_pFace")
            .ToDictionary(r => (r.SourceQueueIndex, r.ItemIndex));
        var ordinary = prior.Fallbacks.ToDictionary(r => (r.SourceLoopQueueIndex, r.TargetLoopQueueIndex));
        var results = new List<GElementRuntimeWindingCheck>(); var faces = new List<GElementFacePointWithinFirstSample>();
        string context = "runtime-winding";
        try
        {
            foreach (var face in prior.PriorFirstSample.Faces)
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
                            context = $"runtime-winding:{current.LoopQueueIndex}:{check.LoopQueueIndex}";
                            var result = Evaluate(current.LoopQueueIndex, check.LoopQueueIndex); results.Add(result);
                            value = check with { Outcome = result.Outcome, PointWithinState = result.IndependentPointWithinState };
                        }
                        value = value with { AfterUnresolvedCheck = pending }; checks.Add(value);
                        if (value.PointWithinState == 1) count++;
                        else if (value.PointWithinState != 0 && value.Outcome != "skipped-open") pending = true;
                    }
                    var row = current with { Checks = checks };
                    if (current.Outcome == "pending-first-sample-point-within")
                        row = row with { ResolvedShouldBeCcw = pending ? null : (count & 1) == 0, ContainmentCount = pending ? null : count,
                            Outcome = pending ? current.Outcome : "runtime-independent-winding-parity" };
                    updated.Add(row);
                }
                faces.Add(new(face.FaceQueueIndex, updated));
            }
            bool complete = faces.SelectMany(f => f.Loops).All(l => l.ResolvedShouldBeCcw != null);
            failure = null; return new(prior, prior.PriorFirstSample with { Faces = faces, Complete = complete }, results,
                results.All(r => r.IndependentPointWithinState != null), false, false);
        }
        catch (DecodeFail) { failure = context; return null; }

        GElementRuntimeWindingCheck Evaluate(int sourceQ, int targetQ)
        {
            var previous = ordinary[(sourceQ, targetQ)]; var target = loops[targetQ];
            var edges = new List<GElementRefinedWindingEdge>(); long? sumBits = null; SourceWindingClassification? classified = null;
            int? refinedState = null; string outcome = "unsupported-runtime-refinement";
            GElementRuntimeWindingCheck Result()
            {
                var compare = CompareSourceWindingRuntimeModes(previous.UnrefinedPointWithinState, refinedState);
                return new(sourceQ, targetQ, compare.IndependentState == 0 ? "runtime-independent-winding-outside" : outcome,
                    edges, sumBits, classified, compare.Modes, compare.IndependentState);
            }
            if (previous.Orientation == null || SelectSourceTwoPointPolygonVertices(target, out _) == null) return Result();
            if (nodes[targetQ].ClassId == 1257 && Field(nodes[targetQ], 1257, "m_chainsWithEnvelopes").Items.Count != 0) return Result();
            var query = samples[sourceQ].SampleUvBits; if (query == null || target.FaceQueueIndex is not int targetFace) return Result();
            double total = 0;
            foreach (var edge in target.Edges)
            {
                int side = edge.FaceSide!.Value; var planes = new List<SourceWindingPlane>();
                for (int i = 0; i < 2; i++)
                {
                    if (!faceLinks.TryGetValue((edge.EdgeQueueIndex, i), out var link) || link.Outcome != "own-node"
                        || link.TargetClassId != 1589 || link.TargetQueueIndex is not int fq || (i == side && fq != targetFace)) return Result();
                    var pointer = Field(nodes[fq], 1589, "m_pSurf").Items[0].Pointer;
                    if (pointer?.QueueIndex is not int sq || pointer.ClassId != 522 || !nodes.TryGetValue(sq, out var plane)
                        || plane.ClassId != 522 || plane.Handle != pointer.Handle) return Result();
                    planes.Add(new(Vector(plane, "m_origin"), Vector(plane, "m_xVec"), Vector(plane, "m_yVec")));
                }
                var first = edge.FirstAndLastPoints[0]; var last = edge.FirstAndLastPoints[1];
                SourceRefinedWindingInput input = new([first.Side0Bits, first.Side1Bits], [last.Side0Bits, last.Side1Bits],
                    planes, side, edge.Flags, query, previous.Orientation.Value);
                var refined = IntegrateSourceRefinedWindingSegment(input, 1e-6, out _); if (refined == null) return Result();
                edges.Add(new(edge.EdgeQueueIndex, side, refined));
                if (refined.Integral.Code != 0) { refinedState = 2; outcome = "runtime-refined-integral-error-boundary"; return Result(); }
                total += BitConverter.Int64BitsToDouble(refined.Integral.ValueBits!.Value);
                if (!double.IsFinite(total)) return Result();
            }
            sumBits = BitConverter.DoubleToInt64Bits(total); classified = ClassifySourceWindingTotal(sumBits.Value, out _);
            if (classified == null) return Result();
            refinedState = classified.Code != 0 ? 2 : classified.Location == 0 ? 0 : 1;
            outcome = "runtime-winding-modes-diverge-or-boundary"; return Result();
        }
        static GElementGraphField Field(GElementGraphNode node, int declaring, string name)
        { var fs = node.Fields.Where(f => f.Declaring == declaring && f.Name == name).ToArray(); if (fs.Length != 1) throw Fail; return fs[0]; }
        static IReadOnlyList<long> Vector(GElementGraphNode node, string name)
        {
            var f = Field(node, 522, name); if (f.Count != 3 || f.Items.Count != 3) throw Fail;
            foreach (var item in f.Items) if (item.Values.Count != 1 || item.End - item.Start != 8 || item.Pointer != null || item.Fields.Count != 0) throw Fail;
            return f.Items.Select(i => i.Values[0]).ToArray();
        }
    }
}
