namespace RvtIO.Parsing;

internal static partial class TfBlockDeserializer
{
    internal sealed record GElementEpsIndependentLoop(int FaceQueueIndex, int LoopQueueIndex,
        bool? LegacyShouldBeCcw, bool? PositiveShouldBeCcw, bool? IndependentShouldBeCcw);
    internal sealed record OwnGElementEpsIndependentSamples(int OwnerId, OwnGElementRuntimeWinding Legacy,
        OwnGElementRuntimeWinding Positive, IReadOnlyList<GElementEpsIndependentLoop> Loops,
        bool AllOwnDecisionsEpsIndependent, bool ActualRuntimeVersionInferred, bool CurrentGeometryAuthorityClosed);

    internal static bool? CompareSourceEpsDecisions(bool? legacy, bool? positive) =>
        legacy != null && positive != null && legacy == positive ? legacy : null;

    /// <summary>Both EPS branches evaluated on the same fresh graph instance, never records paired merely by equal
    /// owner ids from different captures. Only known matching shouldBeCCW decisions are folded. Quantitative envelopes,
    /// EPS metadata and branch records stay separate; no actual version is inferred.</summary>
    internal static OwnGElementEpsIndependentSamples? SelectOwnGElementEpsIndependentSamples(
        OwnGElementGeometryGraph graph, out string? failure)
    {
        var legacy = SelectOwnGElementRuntimeWinding(graph, 0, out failure); if (legacy == null) return null;
        var positive = SelectOwnGElementRuntimeWinding(graph, 1, out failure); if (positive == null) return null;
        failure = "eps-branch-topology";
        if (legacy.FirstSample.Faces.Count != positive.FirstSample.Faces.Count) return null;
        var loops = new List<GElementEpsIndependentLoop>();
        for (int i = 0; i < legacy.FirstSample.Faces.Count; i++)
        {
            var a = legacy.FirstSample.Faces[i]; var b = positive.FirstSample.Faces[i];
            if (a.FaceQueueIndex != b.FaceQueueIndex || a.Loops.Count != b.Loops.Count) return null;
            for (int j = 0; j < a.Loops.Count; j++)
            {
                var x = a.Loops[j]; var y = b.Loops[j]; if (x.LoopQueueIndex != y.LoopQueueIndex) return null;
                loops.Add(new(a.FaceQueueIndex, x.LoopQueueIndex, x.ResolvedShouldBeCcw, y.ResolvedShouldBeCcw,
                    CompareSourceEpsDecisions(x.ResolvedShouldBeCcw, y.ResolvedShouldBeCcw)));
            }
        }
        failure = null;
        return new(graph.OwnerId, legacy, positive, loops, loops.All(l => l.IndependentShouldBeCcw != null), false, false);
    }
}
