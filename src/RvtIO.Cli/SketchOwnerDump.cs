using RvtIO.Model;

namespace RvtIO.Cli;

/// <summary>
/// Diagnostic-only, gated behind <c>--sketch-owner-dump &lt;path.csv&gt;</c> (see Program.cs). Never referenced from the standard
/// export path.
/// <para>
/// For every sketch-owning element (any element whose own <see cref="Element.References"/> contains >= 3 OST_SketchLines members, the
/// population <c>ElementBuilder.BuildSketchTopology</c> resolves onto <see cref="Element.SketchLoops"/>) it dumps the owner id (for
/// slab/wall hosts this equals both the IFC Tag and the Revit ElementId, because the sketch lines reference the host directly), the
/// owner's category and each closed loop's member curve (sketch line) element ids in stored order.
/// </para>
/// <para>
/// "Stored order" is the order of the owner's own <see cref="Element.References"/> list, which is filled in serialization order. It
/// differs from the internal <c>HashSet&lt;int&gt;</c> bookkeeping of <c>ElementBuilder.BuildSketchTopology</c>, which does not preserve
/// order. The loop walk (shared-endpoint connectivity, closing at the start vertex) is the algorithm of
/// <c>ElementBuilder.BuildSketchLoops</c>, reimplemented read-only against public <see cref="Element"/> data; it tracks curve element
/// ids instead of coordinate points and is seeded from the References order.
/// </para>
/// <para>Output is plain CSV, one row per (owner, loop, position). Never write a .json path here.</para>
/// </summary>
static class SketchOwnerDump
{
    public static void Run(RevitFileInfo info, string outPath)
    {
        var byId = new Dictionary<int, Element>();
        foreach (var e in info.Elements) byId[e.Id] = e;

        // OST_SketchLines = -2000045. Element.HostId of a sketch line is set by ElementBuilder.BuildSketchTopology to the candidate
        // referencing the most sketch lines, i.e. the owner this dump groups by.
        var linesByOwner = new Dictionary<int, List<Element>>();
        foreach (var e in info.Elements)
        {
            if (e.CategoryId != -2000045 || e.HostId is not { } ownerId) continue;
            if (!linesByOwner.TryGetValue(ownerId, out var list))
                linesByOwner[ownerId] = list = new List<Element>();
            list.Add(e);
        }

        using var w = new StreamWriter(outPath, false, System.Text.Encoding.UTF8);
        w.WriteLine("OwnerId,OwnerCategory,LoopIndex,LoopSize,Ordinal,CurveId");

        int ownerCount = 0, loopCount = 0;
        foreach (var (ownerId, lines) in linesByOwner.OrderBy(kv => kv.Key))
        {
            if (!byId.TryGetValue(ownerId, out var owner)) continue;

            var lineIdSet = new HashSet<int>(lines.Select(l => l.Id));
            var storedOrder = new List<int>();
            foreach (int r in owner.References ?? Enumerable.Empty<int>())
                if (lineIdSet.Contains(r) && !storedOrder.Contains(r))
                    storedOrder.Add(r);
            // Every member line should already be in the owner's References (that is how HostId was resolved); a missed one is appended
            // so that no curve is dropped from the dump.
            foreach (var l in lines)
                if (!storedOrder.Contains(l.Id)) storedOrder.Add(l.Id);

            var loops = WalkLoops(storedOrder, byId);
            if (loops.Count == 0) continue;
            ownerCount++;
            string category = owner.Category ?? "";
            for (int li = 0; li < loops.Count; li++)
            {
                loopCount++;
                for (int oi = 0; oi < loops[li].Count; oi++)
                    w.WriteLine($"{ownerId},{category},{li},{loops[li].Count},{oi},{loops[li][oi]}");
            }
        }
        Console.Error.WriteLine($"[sketch-owner-dump] {ownerCount} owners, {loopCount} loops -> {outPath}");
    }

    /// <summary>Same connectivity walk as ElementBuilder.BuildSketchLoops (shared endpoints,
    /// closes when back at the loop's start vertex) but keeps curve element ids instead of
    /// coordinate points, and is seeded/ordered strictly by the caller's supplied order.</summary>
    private static List<List<int>> WalkLoops(List<int> orderedLineIds, IReadOnlyDictionary<int, Element> byId)
    {
        static (long, long, long) Key(double[] p) =>
            ((long)Math.Round(p[0] * 1000), (long)Math.Round(p[1] * 1000), (long)Math.Round(p[2] * 1000));

        var adj = new Dictionary<(long, long, long), List<(int id, double[] here, double[] other)>>();
        var segs = new Dictionary<int, (double[] a, double[] b)>();
        foreach (var id in orderedLineIds)
        {
            if (!byId.TryGetValue(id, out var e) || e.Location?.StartPoint == null || e.Location.EndPoint == null)
                continue;
            var a = e.Location.StartPoint;
            var b = e.Location.EndPoint;
            segs[id] = (a, b);
            void Add(double[] from, double[] to)
            {
                var k = Key(from);
                if (!adj.TryGetValue(k, out var list)) adj[k] = list = new();
                list.Add((id, from, to));
            }
            Add(a, b);
            Add(b, a);
        }

        var loops = new List<List<int>>();
        var used = new HashSet<int>();
        foreach (var startId in orderedLineIds)
        {
            if (!segs.TryGetValue(startId, out var startSeg) || used.Contains(startId)) continue;
            var (sa, sb) = startSeg;
            var ids = new List<int> { startId };
            used.Add(startId);
            var startKey = Key(sa);
            var curKey = Key(sb);
            for (int guard = 0; guard < orderedLineIds.Count + 1; guard++)
            {
                if (curKey == startKey) break;
                if (!adj.TryGetValue(curKey, out var candidates)) break;
                int nextId = -1;
                double[]? nextOther = null;
                foreach (var (id, _, other) in candidates)
                    if (!used.Contains(id)) { nextId = id; nextOther = other; break; }
                if (nextId < 0) break;
                used.Add(nextId);
                ids.Add(nextId);
                curKey = Key(nextOther!);
            }
            if (ids.Count >= 3) loops.Add(ids);
        }
        return loops;
    }
}
