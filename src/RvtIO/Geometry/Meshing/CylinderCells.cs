namespace RvtIO.Geometry.Meshing;

/// <summary>A point of a leaf polygon: parameter position, whether it is a clip vertex that may be removed again, and the 3D position
/// it was given (clip points of a loop segment: chord interpolation of the segment ends).</summary>
internal readonly record struct CellPoint(double X, double Y, bool Removable, double[]? Pos3);

/// <summary>A directed loop segment in node coordinates with the 3D positions of its end vertices.</summary>
internal sealed record LoopSegment((double X, double Y) A, (double X, double Y) B, double[] A3, double[] B3);

/// <summary>
/// The loop segments inserted into the leaves of the grid (every segment is clipped to the leaf box; the pieces of a leaf are
/// chained into the clockwise polygons of the leaf, walking along the cell border where the loop leaves the box).
/// </summary>
internal sealed class CylinderLeaf
{
    private const double Eps = KdTree.Eps;

    internal readonly KdTree.Node Node;
    internal readonly List<CellPoint> Verts = new();
    private readonly List<(int Start, int End)> edges = new();

    internal CylinderLeaf(KdTree.Node node) { Node = node; }

    internal static bool Eq((double X, double Y) p, (double X, double Y) q) => Hyp(p.X - q.X, p.Y - q.Y) <= Eps;

    private static double Hyp(double x, double y) => Math.Sqrt(x * x + y * y);

    private int Vid((double X, double Y) p, bool removable, double[]? pos3)
    {
        for (int i = 0; i < Verts.Count; i++)
        {
            var v = Verts[i];
            if (Eq((v.X, v.Y), p))
            {
                // a loop vertex is never removed, even when a clip point coincides with it
                if (!removable && v.Removable) Verts[i] = v with { Removable = false };
                return i;
            }
        }
        Verts.Add(new CellPoint(p.X, p.Y, removable, pos3));
        return Verts.Count - 1;
    }

    private void Add((double X, double Y) pa, (double X, double Y) pb, bool ain, bool bin, double[]? pa3, double[]? pb3)
    {
        int i = Vid(pa, !ain, pa3);
        int j = Vid(pb, !bin, pb3);
        edges.Add((i, j));
    }

    /// <summary>Clips a->b to the closed box; clip points take the border value. Null when nothing remains.</summary>
    private static (( double X, double Y) Pa, (double X, double Y) Pb, bool AIn, bool BIn, double T0, double T1)? Clip(
        (double X, double Y) a, (double X, double Y) b, double x0, double y0, double x1, double y1)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t0 = 0.0, t1 = 1.0;
        Span<(double P, double Q)> planes = stackalloc (double, double)[]
        {
            (-dx, a.X - x0), (dx, x1 - a.X), (-dy, a.Y - y0), (dy, y1 - a.Y),
        };
        foreach (var (p, q) in planes)
        {
            if (p == 0.0)
            {
                if (q < -Eps) return null;
            }
            else
            {
                double r = q / p;
                if (p < 0.0)
                {
                    if (r > t1) return null;
                    if (r > t0) t0 = r;
                }
                else
                {
                    if (r < t0) return null;
                    if (r < t1) t1 = r;
                }
            }
        }
        if (t1 - t0 <= 0.0) return null;
        var pa = a; var pb = b; bool ain = true, bin = true;
        if (t0 > 0.0) { ain = false; pa = (a.X + t0 * dx, a.Y + t0 * dy); }
        if (t1 < 1.0) { bin = false; pb = (a.X + t1 * dx, a.Y + t1 * dy); }
        (double X, double Y) Snap((double X, double Y) p)
        {
            double x = p.X, y = p.Y;
            if (Math.Abs(x - x0) <= Eps) x = x0; else if (Math.Abs(x - x1) <= Eps) x = x1;
            if (Math.Abs(y - y0) <= Eps) y = y0; else if (Math.Abs(y - y1) <= Eps) y = y1;
            return (x, y);
        }
        if (!ain) { pa = Snap(pa); if (Eq(pa, a)) { pa = a; ain = true; } }
        if (!bin) { pb = Snap(pb); if (Eq(pb, b)) { pb = b; bin = true; } }
        if (Eq(pa, pb)) return null;
        return (pa, pb, ain, bin, t0, t1);
    }

    /// <summary>A vertex within Eps of a grid point takes the exact grid point coordinates.</summary>
    private static (double X, double Y) SnapTree(List<(double X, double Y)> treePts, (double X, double Y) p)
    {
        foreach (var q in treePts)
            if (Eq(p, q)) return q;
        return p;
    }

    internal static SortedDictionary<int, CylinderLeaf> Distribute(KdTree tree, IReadOnlyList<LoopSegment> segs)
    {
        var leaves = new SortedDictionary<int, CylinderLeaf>();
        for (int i = 0; i < tree.Nodes.Count; i++)
            if (tree.Nodes[i].C0 < 0) leaves[i] = new CylinderLeaf(tree.Nodes[i]);
        foreach (var sg in segs)
            foreach (var lf in leaves.Values)
            {
                var n = lf.Node;
                if (Clip(sg.A, sg.B, n.X0, n.Y0, n.X1, n.Y1) is not { } r) continue;
                double[]? pa3 = null, pb3 = null;
                if (!r.AIn) pa3 = Lerp(sg.A3, sg.B3, r.T0);
                if (!r.BIn) pb3 = Lerp(sg.A3, sg.B3, r.T1);
                lf.Add(SnapTree(tree.Pts, r.Pa), SnapTree(tree.Pts, r.Pb), r.AIn, r.BIn, pa3, pb3);
            }
        return leaves;
    }

    private static double[] Lerp(double[] a, double[] b, double t) =>
        new[] { a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]), a[2] + t * (b[2] - a[2]) };

    /// <summary>Border side of a point on the leaf box: 1 left, 2 right, 4 bottom, 8 top, 0 inside (a corner belongs to the side
    /// the walk continues on).</summary>
    private static int SideOf((double X, double Y) p, KdTree.Node n)
    {
        int m = 0;
        if (Math.Abs(p.X - n.X0) <= Eps) m = 1; else if (Math.Abs(p.X - n.X1) <= Eps) m = 2;
        if (Math.Abs(p.Y - n.Y0) <= Eps) m |= 4; else if (Math.Abs(p.Y - n.Y1) <= Eps) m |= 8;
        return m switch { 9 => 8, 10 => 2, 6 => 4, 5 => 1, _ => m };
    }

    /// <summary>The polygons of the leaf, or null when no loop segment enters it. A polygon starts at the first unvisited edge and
    /// follows the loop; where the loop leaves the cell the walk continues along the border to the next loop edge entering it.</summary>
    internal List<List<CellPoint>>? Walk()
    {
        var n = Node;
        if (edges.Count == 0) return null;
        var next = new int[Verts.Count];
        Array.Fill(next, -1);
        for (int k = 0; k < edges.Count; k++)
            if (next[edges[k].Start] < 0) next[edges[k].Start] = k;
        var visited = new bool[edges.Count];
        var polys = new List<List<CellPoint>>();
        while (true)
        {
            int k = Array.IndexOf(visited, false);
            if (k < 0) break;
            var poly = new List<CellPoint> { Verts[edges[k].Start] };
            bool ok = true, closed = false;
            while (true)
            {
                if (visited[k]) { ok = false; break; }
                visited[k] = true;
                var ev = Verts[edges[k].End];
                if (Eq((ev.X, ev.Y), (poly[0].X, poly[0].Y))) { closed = true; break; }
                poly.Add(ev);
                int ne = next[edges[k].End];
                if (ne >= 0)
                {
                    if (!visited[ne]) { k = ne; continue; }
                    int alt = -1;
                    for (int i = 0; i < edges.Count; i++)
                        if (!visited[i] && edges[i].Start == edges[k].End) { alt = i; break; }
                    if (alt >= 0) { k = alt; continue; }
                }
                int side = SideOf((ev.X, ev.Y), n);
                if (side == 0) { ok = false; break; }
                int tries = 0;
                var cur = (X: ev.X, Y: ev.Y);
                int found = -1;
                while (true)
                {
                    double bd = 1.0e300;
                    int best = -1;
                    for (int i = 0; i < edges.Count; i++)
                    {
                        var sv = Verts[edges[i].Start];
                        if (visited[i] && !Eq((sv.X, sv.Y), (poly[0].X, poly[0].Y))) continue;
                        if (SideOf((sv.X, sv.Y), n) != side) continue;
                        double d = side == 4 ? cur.X - sv.X : side == 2 ? cur.Y - sv.Y : side == 1 ? sv.Y - cur.Y : sv.X - cur.X;
                        if (d >= -Eps && bd > d)
                        {
                            d = Math.Max(d, 0.0);
                            bd = d; best = i;
                        }
                    }
                    if (best >= 0) { found = best; break; }
                    (int nextSide, (double X, double Y) cp) = side switch
                    {
                        4 => (1, (n.X0, n.Y0)),
                        1 => (8, (n.X0, n.Y1)),
                        8 => (2, (n.X1, n.Y1)),
                        _ => (4, (n.X1, n.Y0)),
                    };
                    side = nextSide;
                    cur = cp;
                    var last = poly[^1];
                    if (!Eq((last.X, last.Y), cp))
                    {
                        if (Eq(cp, (poly[0].X, poly[0].Y))) { closed = true; break; }
                        poly.Add(new CellPoint(cp.X, cp.Y, false, null));
                    }
                    tries++;
                    if (tries >= 5) { ok = false; break; }
                }
                if (closed || !ok) break;
                var s0 = Verts[edges[found].Start];
                if (Eq((s0.X, s0.Y), (poly[0].X, poly[0].Y))) { closed = true; break; }
                var l2 = poly[^1];
                if (Eq((l2.X, l2.Y), (s0.X, s0.Y))) poly[^1] = s0; else poly.Add(s0);
                k = found;
            }
            if (closed && ok && poly.Count >= 3) polys.Add(poly);
        }
        return polys;
    }
}
