namespace RvtIO.Geometry.Meshing;

/// <summary>A polygon vertex of the freeform grid builder. F17 = 1 marks a crossing of a loop segment with a grid line, Pid is the
/// index in the global grid point pool, Seq the creation order of the vertices that are not pool points.</summary>
internal sealed class FfVtx
{
    internal double U, V;
    internal Pt3? P3;
    internal int F17;
    internal int Pid = -1, Seq = -1, PoolHint = -1;
    internal FfVtx(double u, double v, Pt3? p3 = null) { U = u; V = v; P3 = p3; }
}

internal sealed class FfNode
{
    internal double U0, V0, U1, V1;
    internal int Dir = -1;
    internal double Coord;
    internal FfNode? Lo, Hi;
    internal int Depth;
    internal readonly List<FfVtx> Verts = new();
    /// <summary>Segment records [from vertex, to vertex, flag].</summary>
    internal readonly List<int[]> Recs = new();
    internal readonly List<int> VNext = new();
    internal readonly List<(double U, double V)> Tp = new();
    internal int Idx = -1;
    internal FfNode(double u0, double v0, double u1, double v1, int depth = 0) { U0 = u0; V0 = v0; U1 = u1; V1 = v1; Depth = depth; }
}

/// <summary>
/// The uv grid of the mesher for Ruled surface and NURBS surface faces: a kd-tree of rectangles over the uv box of the loop
/// samples, the clipping of the loops into the leaf cells (clockwise polygons) and the T-junction points between neighbouring
/// cells.
/// </summary>
internal static class FreeformGrid
{
    internal const double Tol = 1e-10;
    internal const int MaxDepth = 12;
    private static readonly double[] Wa = { 0.3102637180713, 0.5, 0.6897362819287 };
    private static readonly double[] Wb = { 0.6897362819287, 0.5, 0.3102637180713 };

    private static double D2(Pt3 a, Pt3 b) { var d = a - b; return d.Dot(d); }

    private static double SegClosestD2(Pt3 p, Pt3 a, Pt3 b)
    {
        var ab = b - a;
        double l = ab.Dot(ab);
        if (l == 0.0) return D2(p, a);
        double t = (p - a).Dot(ab) / l;
        t = Math.Min(1.0, Math.Max(0.0, t));
        return D2(p, a + ab * t);
    }

    /// <summary>The split decision of a rectangle: 0 = split in u, 1 = split in v, -1 = leaf.</summary>
    internal static int SplitDir(Func<double, double, Pt3> s, double u0, double u1, double v0, double v1, double surfTol, double maxEdge)
    {
        double tol2 = surfTol * surfTol;
        double um = (u0 + u1) * 0.5, vm = (v0 + v1) * 0.5;
        var sUmV0 = s(um, v0); var sU0Vm = s(u0, vm); var sU1Vm = s(u1, vm); var sUmV1 = s(um, v1);
        double devU = 0.0, devV = 0.0;
        for (int k = 0; k < 3; k++)
        {
            double wa = Wa[k], wb = Wb[k];
            var pu = s(wa * u0 + wb * u1, vm);
            var cu = wa * sU0Vm + wb * sU1Vm;
            double du = D2(cu, pu);
            var pv = s(um, wa * v0 + wb * v1);
            var cv = wa * sUmV0 + wb * sUmV1;
            double dv = D2(cv, pv);
            if (k > 0) { du = Math.Max(du, devU); dv = Math.Max(dv, devV); }
            devU = du; devV = dv;
        }
        if (tol2 < devU || tol2 < devV) return devU <= devV ? 1 : 0;
        var p00 = s(u0, v0); var p01 = s(u0, v1); var p10 = s(u1, v0); var p11 = s(u1, v1);
        var m1 = (p00 + p11) * 0.5; var m2 = (p01 + p10) * 0.5;
        double d1 = Math.Min(SegClosestD2(m1, sUmV0, sUmV1), SegClosestD2(m1, sU0Vm, sU1Vm));
        double d2 = Math.Min(SegClosestD2(m2, sUmV0, sUmV1), SegClosestD2(m2, sU0Vm, sU1Vm));
        if (100.0 * tol2 < d1 || 100.0 * tol2 < d2) return d1 <= d2 ? 1 : 0;
        double me2 = maxEdge * maxEdge;
        if (D2(p11, p01) > me2) return 0;
        if (D2(p10, p00) > me2) return 0;
        if (D2(p11, p10) > me2) return 1;
        if (D2(p01, p00) > me2) return 1;
        return -1;
    }

    internal static FfNode BuildTree(Func<double, double, Pt3> s, double uLo, double uHi, double vLo, double vHi, double surfTol, double maxEdge)
    {
        var root = new FfNode(uLo, vLo, uHi, vHi);
        var stack = new Stack<FfNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Depth > MaxDepth) continue;
            int d = SplitDir(s, n.U0, n.U1, n.V0, n.V1, surfTol, maxEdge);
            if (d < 0) continue;
            if (d == 0)
            {
                double m = (n.U0 + n.U1) * 0.5;
                if (Math.Abs(m - n.U0) <= Tol || Math.Abs(m - n.U1) <= Tol) continue;
                n.Dir = 0; n.Coord = m;
                n.Lo = new FfNode(n.U0, n.V0, m, n.V1, n.Depth + 1); n.Hi = new FfNode(m, n.V0, n.U1, n.V1, n.Depth + 1);
            }
            else
            {
                double m = (n.V0 + n.V1) * 0.5;
                if (Math.Abs(m - n.V0) <= Tol || Math.Abs(m - n.V1) <= Tol) continue;
                n.Dir = 1; n.Coord = m;
                n.Lo = new FfNode(n.U0, n.V0, n.U1, m, n.Depth + 1); n.Hi = new FfNode(n.U0, m, n.U1, n.V1, n.Depth + 1);
            }
            stack.Push(n.Hi); stack.Push(n.Lo);
        }
        return root;
    }

    private static void TpDescend(FfNode node, (double U, double V) pt, FfNode skip)
    {
        if (node == skip) return;
        if (node.Lo != null)
        {
            foreach (var ch in new[] { node.Lo, node.Hi! })
                if (ch.U0 - Tol <= pt.U && pt.U <= ch.U1 + Tol && ch.V0 - Tol <= pt.V && pt.V <= ch.V1 + Tol) TpDescend(ch, pt, skip);
            return;
        }
        bool onV = Math.Abs(pt.U - node.U0) <= Tol || Math.Abs(pt.U - node.U1) <= Tol;
        bool onH = Math.Abs(pt.V - node.V0) <= Tol || Math.Abs(pt.V - node.V1) <= Tol;
        if (onV != onH) node.Tp.Add(pt);
    }

    /// <summary>Both end points of the split line of every inner non-root node become T-junction points of the neighbouring leaves.</summary>
    internal static void AddTpoints(FfNode root)
    {
        var st = new Stack<FfNode>();
        var inner = new List<FfNode>();
        st.Push(root);
        while (st.Count > 0)
        {
            var n = st.Pop();
            if (n.Lo == null) continue;
            if (n != root) inner.Add(n);
            st.Push(n.Hi!); st.Push(n.Lo);
        }
        foreach (var n in inner)
        {
            var pts = n.Dir == 1 ? new[] { (n.U0, n.Coord), (n.U1, n.Coord) } : new[] { (n.Coord, n.V0), (n.Coord, n.V1) };
            foreach (var p in pts) TpDescend(root, p, n);
        }
    }

    internal static (long, long) Key(double u, double v) => ((long)Math.Round(u * 1e9), (long)Math.Round(v * 1e9));

    /// <summary>
    /// Node indices and the global point pool in the order of the mesher: the root owns the corners BL, TL, TR, BR; a split
    /// appends two points and two children (lower first); the stack pops the upper child first.
    /// </summary>
    internal static (List<FfNode> Nodes, Dictionary<(long, long), int> Ids) AssignPool(FfNode root)
    {
        root.Idx = 0;
        var nodes = new List<FfNode> { root };
        var pool = new List<(double, double)> { (root.U0, root.V0), (root.U0, root.V1), (root.U1, root.V1), (root.U1, root.V0) };
        var stack = new Stack<FfNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Lo == null) continue;
            n.Lo.Idx = nodes.Count; n.Hi!.Idx = nodes.Count + 1;
            nodes.Add(n.Lo); nodes.Add(n.Hi);
            if (n.Dir == 1) { pool.Add((n.U1, n.Coord)); pool.Add((n.U0, n.Coord)); }
            else { pool.Add((n.Coord, n.V1)); pool.Add((n.Coord, n.V0)); }
            stack.Push(n.Lo); stack.Push(n.Hi);
        }
        var ids = new Dictionary<(long, long), int>();
        for (int i = 0; i < pool.Count; i++) ids.TryAdd(Key(pool[i].Item1, pool[i].Item2), i);
        return (nodes, ids);
    }

    internal static bool Eq(double au, double av, double bu, double bv)
    {
        double dx = au - bu, dy = av - bv;
        return Math.Sqrt(dx * dx + dy * dy) <= Tol;
    }

    private static bool Eq(FfVtx a, FfVtx b) => Eq(a.U, a.V, b.U, b.V);

    private static int FindOrAdd(FfNode cell, FfVtx vtx)
    {
        for (int i = cell.Verts.Count - 1; i >= 0; i--)
        {
            var w = cell.Verts[i];
            if ((w.U - vtx.U) * (w.U - vtx.U) + (w.V - vtx.V) * (w.V - vtx.V) < Tol * Tol) return i;
        }
        cell.Verts.Add(vtx);
        cell.VNext.Add(-1);
        return cell.Verts.Count - 1;
    }

    /// <summary>Inserts the segment A to B (a reversed loop segment) into a leaf cell; a segment running along the cell border in the
    /// counter-clockwise direction is dropped.</summary>
    private static void CellInsert(FfNode cell, FfVtx a, FfVtx b, int flag)
    {
        if (Eq(a, b)) return;
        if (Math.Abs(a.U - b.U) <= Tol)
        {
            if (Math.Abs(a.U - cell.U0) <= Tol && a.V > b.V) return;
            if (Math.Abs(a.U - cell.U1) <= Tol && b.V > a.V) return;
        }
        if (Math.Abs(a.V - b.V) <= Tol)
        {
            if (Math.Abs(a.V - cell.V0) <= Tol && b.U > a.U) return;
            if (Math.Abs(a.V - cell.V1) <= Tol && a.U > b.U) return;
        }
        int ia = FindOrAdd(cell, a);
        int ib = FindOrAdd(cell, b);
        if (ia != ib)
        {
            cell.Recs.Add(new[] { ia, ib, flag });
            cell.VNext[ia] = cell.Recs.Count - 1;
        }
    }

    /// <summary>Walks the segment A to B down the tree; a crossing of a split line makes a new vertex (flag F17) with the linearly
    /// interpolated 3D point.</summary>
    internal static void SegToCells(FfNode node, FfVtx a, FfVtx b, int flag, Func<double, double, Pt3> s)
    {
        if (node.Lo == null) { CellInsert(node, a, b, flag); return; }
        int d = node.Dir;
        double c = node.Coord;
        double ta = d == 0 ? a.U : a.V, tb = d == 0 ? b.U : b.V;
        bool aOn = Math.Abs(ta - c) <= Tol, bOn = Math.Abs(tb - c) <= Tol;
        if (aOn && bOn)
        {
            SegToCells(node.Lo, a, b, flag, s);
            SegToCells(node.Hi!, a, b, flag, s);
            return;
        }
        if (aOn) { SegToCells(tb > c ? node.Hi! : node.Lo, a, b, flag, s); return; }
        if (bOn) { SegToCells(ta > c ? node.Hi! : node.Lo, a, b, flag, s); return; }
        if (c > ta != c > tb)
        {
            double oa = d == 0 ? a.V : a.U, ob = d == 0 ? b.V : b.U;
            double co = (ob - oa) * (c - ta) / (tb - ta) + oa;
            (double cu, double cv) = d == 0 ? (c, co) : (co, c);
            double dx = a.U - b.U, dy = a.V - b.V;
            double l = Math.Sqrt(dx * dx + dy * dy);
            double ex = cu - a.U, ey = cv - a.V;
            double ratio = Math.Sqrt(ex * ex + ey * ey) / l;
            var pa = a.P3 ?? s(a.U, a.V);
            var pb = b.P3 ?? s(b.U, b.V);
            var cross = new FfVtx(cu, cv, pa + (pb - pa) * ratio) { F17 = 1 };
            var first = ta > c ? node.Hi! : node.Lo;
            var second = ta > c ? node.Lo : node.Hi!;
            SegToCells(first, a, cross, flag, s);
            SegToCells(second, cross, b, flag, s);
            return;
        }
        SegToCells(ta < c ? node.Lo : node.Hi!, a, b, flag, s);
    }

    /// <summary>Number of split lines of the tree crossed by the segment A to B (points on a split line are not counted).</summary>
    internal static int CountCross(FfNode node, (double U, double V) a, (double U, double V) b)
    {
        if (node.Lo == null) return 0;
        int d = node.Dir;
        double c = node.Coord;
        double ta = d == 0 ? a.U : a.V, tb = d == 0 ? b.U : b.V;
        bool aOn = Math.Abs(ta - c) <= Tol, bOn = Math.Abs(tb - c) <= Tol;
        if (aOn && bOn) return CountCross(node.Lo, a, b) + CountCross(node.Hi!, a, b);
        if (aOn) return CountCross(tb > c ? node.Hi! : node.Lo, a, b);
        if (bOn) return CountCross(ta > c ? node.Hi! : node.Lo, a, b);
        if (c > ta != c > tb)
        {
            double oa = d == 0 ? a.V : a.U, ob = d == 0 ? b.V : b.U;
            double co = (ob - oa) * (c - ta) / (tb - ta) + oa;
            var cr = d == 0 ? (c, co) : (co, c);
            var first = ta > c ? node.Hi! : node.Lo;
            var second = ta > c ? node.Lo : node.Hi!;
            return 1 + CountCross(first, a, cr) + CountCross(second, cr, b);
        }
        return CountCross(ta < c ? node.Lo : node.Hi!, a, b);
    }

    private static int SideCode(FfNode cell, double u, double v)
    {
        int code = 0;
        if (Math.Abs(u - cell.U0) <= Tol) code |= 1;
        else if (Math.Abs(u - cell.U1) <= Tol) code |= 2;
        if (Math.Abs(v - cell.V0) <= Tol) code |= 4;
        else if (Math.Abs(v - cell.V1) <= Tol) code |= 8;
        return code;
    }

    // a corner as the end of a walk continues along the next clockwise side, as the start of a segment it leaves along this side
    private static int EndRemap(int code) => code switch { 9 => 8, 10 => 2, 6 => 4, 5 => 1, _ => code };
    private static int StartRemap(int code) => code switch { 9 => 1, 10 => 8, 6 => 2, 5 => 4, _ => code };

    /// <summary>The clockwise polygons of a cell: start at the first unvisited record, follow the records, and where a chain ends on
    /// the border walk the border clockwise to the nearest segment start (inserting the corners passed).</summary>
    internal static List<List<FfVtx>> WalkCell(FfNode cell)
    {
        var polys = new List<List<FfVtx>>();
        var recs = cell.Recs;
        var verts = cell.Verts;
        if (recs.Count == 0) return polys;
        double u0 = cell.U0, v0 = cell.V0, u1 = cell.U1, v1 = cell.V1;
        var visited = new bool[recs.Count];
        while (true)
        {
            int r = -1;
            for (int i = 0; i < recs.Count && r < 0; i++) if (!visited[i] && recs[i][2] == 0) r = i;
            for (int i = 0; i < recs.Count && r < 0; i++) if (!visited[i]) r = i;
            if (r < 0) break;
            var poly = new List<FfVtx> { verts[recs[r][0]] };
            bool dropped = false;
            while (true)
            {
                if (visited[r]) break;
                visited[r] = true;
                int ve = recs[r][1];
                var vt = verts[ve];
                if (Eq(vt, poly[0])) break;
                poly.Add(vt);
                int nx = cell.VNext[ve];
                if (nx >= 0) { r = nx; continue; }
                int code = SideCode(cell, vt.U, vt.V);
                if (code == 0) { dropped = true; break; }
                int side = EndRemap(code);
                double coord = side is 4 or 8 ? vt.U : vt.V;
                int found = -1, guard = 0;
                while (found == -1)
                {
                    if (++guard > 8) { dropped = true; break; }
                    double best = 1e300;
                    int bi = -1;
                    for (int k = 0; k < recs.Count; k++)
                    {
                        var w = verts[recs[k][0]];
                        if (visited[k] && !Eq(w, poly[0])) continue;
                        if (StartRemap(SideCode(cell, w.U, w.V)) != side) continue;
                        double dd;
                        switch (side)
                        {
                            case 1: dd = w.V - coord; break;
                            case 8: dd = w.U - coord; break;
                            case 4: dd = coord - w.U; break;
                            case 2: dd = coord - w.V; break;
                            default: continue;
                        }
                        if (dd >= 0.0 && best > dd) { best = dd; bi = k; }
                    }
                    if (bi >= 0) { found = bi; break; }
                    double cu, cv, ncoord;
                    int nside;
                    switch (side)
                    {
                        case 1: (cu, cv, nside, ncoord) = (u0, v1, 8, u0); break;
                        case 8: (cu, cv, nside, ncoord) = (u1, v1, 2, v1); break;
                        case 2: (cu, cv, nside, ncoord) = (u1, v0, 4, u1); break;
                        case 4: (cu, cv, nside, ncoord) = (u0, v0, 1, v0); break;
                        default: throw new InvalidOperationException("cell border walk left the four sides");
                    }
                    var corner = new FfVtx(cu, cv);
                    if (!Eq(poly[^1], corner))
                    {
                        if (Eq(corner, poly[0])) { found = -2; break; }
                        poly.Add(corner);
                    }
                    side = nside; coord = ncoord;
                }
                if (dropped || found == -2) break;
                r = found;
                if (visited[r] && Eq(verts[recs[r][0]], poly[0])) break;
                if (!Eq(verts[recs[r][0]], poly[^1])) poly.Add(verts[recs[r][0]]);
            }
            if (dropped) continue;
            if (poly.Count >= 2 && Eq(poly[^1], poly[0])) poly.RemoveAt(poly.Count - 1);
            polys.Add(poly);
        }
        return polys;
    }
}
