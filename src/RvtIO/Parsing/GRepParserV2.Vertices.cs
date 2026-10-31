namespace RvtIO.Parsing;

internal static partial class GRepParserV2
{
    /// <summary>
    /// The end points of the edges are the vertices of Revit (see <see
    /// cref="MeshVertexTable"/>); RVTIO_X_VERT=0 switches the model off.
    /// </summary>
    private static readonly bool MeshVertexModel = Environment.GetEnvironmentVariable("RVTIO_X_VERT") != "0";

    /// <summary>
    /// RVTIO_X_TOPO=1: the vertex offers follow the links of the loops and the flags of the Edges
    /// (<see cref="MeshConnectedSlots"/>, the order of the connected edges) instead of
    /// the distance of the end points.
    /// </summary>
    private static readonly bool MeshTopologicalOffers = Environment.GetEnvironmentVariable("RVTIO_X_TOPO") == "1";

    /// <summary>
    /// The slot propagation of Revit: the ends are requested in the given order of the
    /// Edges (first end, then last end); a request of an empty slot takes the own evaluation of the
    /// slot and offers it to the neighbouring slots.
    /// </summary>
    internal static Dictionary<(uint Edge, int End), double[]> MeshShareVertices(IReadOnlyList<uint> order, Func<uint, int, double[]?> own,
        IReadOnlyDictionary<(uint, int), List<(uint, int)>> neighbours)
    {
        var slots = new Dictionary<(uint Edge, int End), double[]>();
        foreach (uint edge in order)
            for (int end = 0; end < 2; end++)
            {
                if (slots.ContainsKey((edge, end)) || own(edge, end) is not { } vertex) continue;
                slots[(edge, end)] = vertex;
                if (!neighbours.TryGetValue((edge, end), out var offered)) continue;
                foreach (var (handle, otherEnd) in offered)
                    if (!slots.ContainsKey((handle, otherEnd)) && own(handle, otherEnd) is { } candidate && Dist3(candidate, vertex) <= 1e-6)
                        slots[(handle, otherEnd)] = vertex;
            }
        return slots;
    }

    /// <summary>
    /// The slots a new vertex of the slot (e, which) is offered to, in the order of
    /// The Edge that follows e in the loop of its first Face and
    /// the one that precedes it in the loop of the second.
    /// </summary>
    internal static List<(uint, int)> MeshConnectedSlots(EdgeRec e, int which, Dictionary<uint, EdgeRec?> edges, Dictionary<uint, (uint First, uint Last, uint Face)> loopBySentinel)
    {
        var offers = new List<(uint, int)>(2);
        void Connected(uint link, uint face, int previous)
        {
            uint handle = link;
            if (loopBySentinel.TryGetValue(link, out var loop)) { handle = previous != 0 ? loop.Last : loop.First; face = loop.Face; }
            if (handle == 0 || !edges.TryGetValue(handle, out var n) || n == null) return;
            int slot = previous ^ (n.Flags & 1);
            if (face == n.FaceB) slot ^= 1;
            else if (face != n.FaceA) return;
            offers.Add((handle, slot));
        }
        bool last = (e.Flags & 1) != 0 ? which == 0 : which != 0;
        if (last) { Connected(e.NextA, e.FaceA, 0); Connected(e.PrevB, e.FaceB, 1); }
        else { Connected(e.PrevA, e.FaceA, 1); Connected(e.NextB, e.FaceB, 0); }
        return offers;
    }

    /// <summary>
    /// The 3D end points of the Edges as Revit has them. Every Edge owns one vertex slot per end.
    /// </summary>
    private sealed class MeshVertexTable
    {
        private Dictionary<(uint Edge, int End), double[]> _value = new();

        internal bool TryGet(uint edge, int end, out double[] value) => _value.TryGetValue((edge, end), out value!);

        internal static MeshVertexTable Build(Cur c, Dictionary<uint, EdgeRec?> edges, Dictionary<uint, SurfaceRec> surfaces, HashSet<uint> activeFaces,
            Dictionary<uint, List<int>> loopsByFace)
        {
            var table = new MeshVertexTable();
            // the slots that meet in a vertex: the ends of consecutive Edges of a loop
            var neighbours = new Dictionary<(uint, int), List<(uint, int)>>();
            void Link((uint, int) a, (uint, int) b)
            {
                if (!neighbours.TryGetValue(a, out var la)) neighbours[a] = la = new List<(uint, int)>();
                if (!la.Contains(b)) la.Add(b);
                if (!neighbours.TryGetValue(b, out var lb)) neighbours[b] = lb = new List<(uint, int)>();
                if (!lb.Contains(a)) lb.Add(a);
            }
            var sequence = new List<EdgeRec>();
            var touched = new HashSet<uint>();
            foreach (uint face in activeFaces.OrderBy(h => h))
            {
                if (!surfaces.TryGetValue(face, out var surface) || !loopsByFace.TryGetValue(face, out var loopIndices)) continue;
                foreach (int index in loopIndices)
                {
                    var loop = c.SpaceEdgeLoops[index];
                    uint sentinel = (uint)c.Queue[index].V, current = loop.Next;
                    var walk = new List<(EdgeRec Edge, bool SideA)>();
                    int guard = 0;
                    while (current != sentinel && guard++ < 100000)
                    {
                        if (!edges.TryGetValue(current, out var e) || e == null || !e.HasEndpoints) { walk.Clear(); break; }
                        bool sideA = e.FaceA == face;
                        walk.Add((e, sideA));
                        current = sideA ? e.NextA : e.NextB;
                    }
                    for (int i = 0; i < walk.Count && !MeshTopologicalOffers; i++)
                    {
                        var (ei, sa) = walk[i];
                        var (ej, sb) = walk[(i + 1) % walk.Count];
                        if (ei.Handle == ej.Handle) { Link((ei.Handle, 0), (ei.Handle, 1)); continue; }
                        double[]?[] pa = { Eval(c, face, surface, ei, sa, 0), Eval(c, face, surface, ei, sa, 1) };
                        double[]?[] pb = { Eval(c, face, surface, ej, sb, 0), Eval(c, face, surface, ej, sb, 1) };
                        double best = double.MaxValue; int ba = -1, bb = -1;
                        for (int a = 0; a < 2; a++)
                            for (int b = 0; b < 2; b++)
                                if (pa[a] != null && pb[b] != null && Dist3(pa[a]!, pb[b]!) < best) { best = Dist3(pa[a]!, pb[b]!); ba = a; bb = b; }
                        if (ba >= 0 && best < 1e-6) Link((ei.Handle, ba), (ej.Handle, bb));
                    }
                    foreach (var (e, _) in walk)
                        if (touched.Add(e.Handle)) sequence.Add(e);
                }
            }

            if (MeshTopologicalOffers)
            {
                // the offers of the connected edges, from the links of the loops and
                // the flags of the Edges (no geometry)
                var loopBySentinel = new Dictionary<uint, (uint First, uint Last, uint Face)>();
                foreach (var (loopFace, loopList) in loopsByFace)
                    foreach (int index in loopList)
                        loopBySentinel[(uint)c.Queue[index].V] = (c.SpaceEdgeLoops[index].Next, c.SpaceEdgeLoops[index].Prev, loopFace);
                neighbours = new Dictionary<(uint, int), List<(uint, int)>>();
                foreach (var e in sequence)
                    for (int which = 0; which < 2; which++)
                        neighbours[(e.Handle, which)] = MeshConnectedSlots(e, which, edges, loopBySentinel);
            }
            var byHandle = sequence.ToDictionary(e => e.Handle);
            var own = new Dictionary<(uint, int), double[]?>();
            double[]? Own(uint handle, int end)
            {
                if (own.TryGetValue((handle, end), out var cached)) return cached;
                double[]? value = null;
                if (byHandle.TryGetValue(handle, out var e) && MeshEdgeEvaluationFace(e, 0, activeFaces, surfaces) is { } f && surfaces.TryGetValue(f, out var s))
                    value = Eval(c, f, s, e, e.FaceA == f, end);
                own[(handle, end)] = value;
                return value;
            }

            table._value = MeshShareVertices(sequence.Select(e => e.Handle).ToList(), Own, neighbours);
            return table;
        }

        private static double[]? Eval(Cur c, uint face, SurfaceRec surface, EdgeRec e, bool sideA, int end)
        {
            var p = end == 0 ? e.StoredStart : e.StoredEndP;
            double u = sideA ? p.UA : p.UB, v = sideA ? p.VA : p.VB;
            return double.IsFinite(u) && double.IsFinite(v) ? MeshEvaluate(c, face, surface, u, v) : null;
        }
    }
}
