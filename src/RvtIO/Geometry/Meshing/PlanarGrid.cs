namespace RvtIO.Geometry.Meshing;

/// <summary>The uv grid of the planar face mesher: the binary partition of the face envelope and the clipping of the loops
/// into the leaf cells (clockwise polygons).</summary>
internal static class PlanarGrid
{
    internal const double Eps = 1e-10;

    internal sealed class Cell
    {
        public double U0, V0, U1, V1;
        public int Depth;
        public int Child = -1;
        /// <summary>Grid point ids of (u0,v0) (u0,v1) (u1,v1) (u1,v0).</summary>
        public int[] Corners = null!;
    }

    internal static bool Close((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) <= Eps && Math.Abs(a.Y - b.Y) <= Eps;

    /// <summary>Leaf cells in creation order and the grid points in creation order. A cell is split in u while its u side is
    /// longer than Lmax, then in v; the work list is a stack, so after a split the high child is examined first.</summary>
    internal static (List<Cell> Leaves, List<(double X, double Y)> GridPoints) BspCells(
        (double U0, double V0, double U1, double V1) env, double lmax, double su, double sv)
    {
        var gp = new List<(double X, double Y)> { (env.U0, env.V0), (env.U0, env.V1), (env.U1, env.V1), (env.U1, env.V0) };
        var cells = new List<Cell> { new() { U0 = env.U0, V0 = env.V0, U1 = env.U1, V1 = env.V1, Depth = 0, Corners = new[] { 0, 1, 2, 3 } } };
        var work = new Stack<int>();
        work.Push(0);
        double l2 = lmax * lmax;
        while (work.Count > 0)
        {
            int idx = work.Pop();
            var c = cells[idx];
            if (c.Depth > 12) continue;
            double du = (c.U1 - c.U0) * su, dv = (c.V1 - c.V0) * sv;
            int axis;
            if (du * du > l2) axis = 0;
            else if (dv * dv > l2) axis = 1;
            else continue;
            int c0 = c.Corners[0], c1 = c.Corners[1], c2 = c.Corners[2], c3 = c.Corners[3];
            Cell a, b;
            int n = gp.Count;
            if (axis == 0)
            {
                double mid = (c.U0 + c.U1) * 0.5;
                if (Math.Abs(mid - c.U0) <= Eps || Math.Abs(mid - c.U1) <= Eps) continue;
                gp.Add((mid, c.V1)); gp.Add((mid, c.V0));
                a = new Cell { U0 = c.U0, V0 = c.V0, U1 = mid, V1 = c.V1, Depth = c.Depth + 1, Corners = new[] { c0, c1, n, n + 1 } };
                b = new Cell { U0 = mid, V0 = c.V0, U1 = c.U1, V1 = c.V1, Depth = c.Depth + 1, Corners = new[] { n + 1, n, c2, c3 } };
            }
            else
            {
                double mid = (c.V0 + c.V1) * 0.5;
                if (Math.Abs(mid - c.V0) <= Eps || Math.Abs(mid - c.V1) <= Eps) continue;
                gp.Add((c.U1, mid)); gp.Add((c.U0, mid));
                a = new Cell { U0 = c.U0, V0 = c.V0, U1 = c.U1, V1 = mid, Depth = c.Depth + 1, Corners = new[] { c0, n + 1, n, c3 } };
                b = new Cell { U0 = c.U0, V0 = mid, U1 = c.U1, V1 = c.V1, Depth = c.Depth + 1, Corners = new[] { n + 1, c1, c2, n } };
            }
            c.Child = cells.Count;
            cells.Add(a); cells.Add(b);
            work.Push(c.Child); work.Push(c.Child + 1);
        }
        return (cells.Where(c => c.Child < 0).ToList(), gp);
    }

    /// <summary>Walking order of every loop, starting at its first vertex: outer loops clockwise, holes counter-clockwise (hole =
    /// odd containment depth); a loop stored the other way round is walked backwards.</summary>
    internal static List<List<(double X, double Y)>> LoopArrays(List<List<(double X, double Y)>> uvLoops)
    {
        var hole = new HashSet<int>();
        foreach (var (_, holes) in ClassifyDepth(uvLoops)) hole.UnionWith(holes);
        var res = new List<List<(double X, double Y)>>();
        for (int i = 0; i < uvLoops.Count; i++)
        {
            var p = uvLoops[i];
            bool wantCw = !hole.Contains(i);
            bool isCw = GeTess.SignedArea(p) <= 0;
            if (isCw == wantCw) res.Add(new List<(double X, double Y)>(p));
            else
            {
                var r = new List<(double X, double Y)> { p[0] };
                for (int k = p.Count - 1; k > 0; k--) r.Add(p[k]);
                res.Add(r);
            }
        }
        return res;
    }

    /// <summary>(outer index, hole indices) by containment depth (even depth = outer).</summary>
    private static List<(int Outer, List<int> Holes)> ClassifyDepth(List<List<(double X, double Y)>> polys)
    {
        int n = polys.Count;
        var areas = polys.Select(p => Math.Abs(GeTess.SignedArea(p))).ToArray();
        var parent = new int[n];
        for (int i = 0; i < n; i++)
        {
            int best = -1;
            for (int j = 0; j < n; j++)
                if (i != j && areas[j] > areas[i]
                    && GeTess.InsideEvenOdd(polys[i][0], new IReadOnlyList<(double X, double Y)>[] { polys[j] })
                    && (best < 0 || areas[j] < areas[best])) best = j;
            parent[i] = best;
        }
        var depth = new int[n];
        for (int i = 0; i < n; i++)
        {
            int d = 0, j = parent[i];
            while (j >= 0) { d++; j = parent[j]; }
            depth[i] = d;
        }
        var result = new List<(int, List<int>)>();
        for (int i = 0; i < n; i++)
            if (depth[i] % 2 == 0)
                result.Add((i, Enumerable.Range(0, n).Where(j => parent[j] == i && depth[j] % 2 == 1).ToList()));
        return result;
    }

    /// <summary>The walked edges: the one that arrives at the first vertex first, then backwards.</summary>
    private static IEnumerable<((double X, double Y) P, (double X, double Y) Q)> EdgeOrder(List<(double X, double Y)> a)
    {
        int n = a.Count;
        for (int j = n - 1; j >= 0; j--) yield return (a[j], a[(j + 1) % n]);
    }

    internal readonly record struct Piece((double X, double Y) A, (double X, double Y) B, bool ACut, bool BCut);

    /// <summary>
    /// Clip the walked edge p->q to the cell rectangle. An end point within Eps of the rectangle is kept as it is.
    /// </summary>
    private static Piece? ClipSegment((double X, double Y) p, (double X, double Y) q, (double U0, double V0, double U1, double V1) r)
    {
        var (u0, v0, u1, v1) = r;
        bool Inside((double X, double Y) pt) => u0 - Eps <= pt.X && pt.X <= u1 + Eps && v0 - Eps <= pt.Y && pt.Y <= v1 + Eps;
        bool WrongWayOnBorder((double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            if (Math.Abs(a.X - u0) <= Eps && Math.Abs(b.X - u0) <= Eps) return dy <= 0;
            if (Math.Abs(a.X - u1) <= Eps && Math.Abs(b.X - u1) <= Eps) return dy >= 0;
            if (Math.Abs(a.Y - v0) <= Eps && Math.Abs(b.Y - v0) <= Eps) return dx >= 0;
            if (Math.Abs(a.Y - v1) <= Eps && Math.Abs(b.Y - v1) <= Eps) return dx <= 0;
            return false;
        }
        bool ip = Inside(p), iq = Inside(q);
        if (ip && iq)
        {
            if (Close(p, q) || WrongWayOnBorder(p, q)) return null;
            return new Piece(p, q, false, false);
        }
        double x0 = p.X, y0 = p.Y, x1 = q.X, y1 = q.Y;
        double dxx = x1 - x0, dyy = y1 - y0;
        double t0 = 0.0, t1 = 1.0;
        var clips = new (double Pk, double Qk)[] { (-dxx, x0 - u0), (dxx, u1 - x0), (-dyy, y0 - v0), (dyy, v1 - y0) };
        foreach (var (pk, qk) in clips)
        {
            if (Math.Abs(pk) < 1e-300)
            {
                if (qk < -Eps) return null;
            }
            else
            {
                double t = qk / pk;
                if (pk < 0)
                {
                    if (t > t1) return null;
                    if (t > t0) t0 = t;
                }
                else
                {
                    if (t < t0) return null;
                    if (t < t1) t1 = t;
                }
            }
        }
        if (t1 - t0 <= 0) return null;
        (double X, double Y) Snap((double X, double Y) pt)
        {
            double x = pt.X, y = pt.Y;
            if (Math.Abs(x - u0) <= Eps) x = u0;
            if (Math.Abs(x - u1) <= Eps) x = u1;
            if (Math.Abs(y - v0) <= Eps) y = v0;
            if (Math.Abs(y - v1) <= Eps) y = v1;
            return (x, y);
        }
        bool aCut = !ip, bCut = !iq;
        var a = aCut ? Snap((x0 + dxx * t0, y0 + dyy * t0)) : p;
        var b = bCut ? Snap((x0 + dxx * t1, y0 + dyy * t1)) : q;
        if (Close(a, b) || WrongWayOnBorder(a, b)) return null;
        return new Piece(a, b, aCut, bCut);
    }

    /// <summary>Cell side code of a point (1 left, 2 right, 4 bottom, 8 top); a corner belongs to the side that follows it in the
    /// clockwise walk when start is true, to the side that precedes it otherwise.</summary>
    private static int SideOf((double X, double Y) pt, (double U0, double V0, double U1, double V1) r, bool start)
    {
        int code = 0;
        if (Math.Abs(pt.X - r.U0) <= Eps) code = 1;
        else if (Math.Abs(pt.X - r.U1) <= Eps) code = 2;
        if (Math.Abs(pt.Y - r.V0) <= Eps) code |= 4;
        else if (Math.Abs(pt.Y - r.V1) <= Eps) code |= 8;
        if (start)
            return code switch { 9 => 8, 10 => 2, 6 => 4, 5 => 1, _ => code };
        return code switch { 9 => 1, 10 => 8, 6 => 2, 5 => 4, _ => code };
    }

    /// <summary>
    /// Chain the clipped pieces into closed polygons: from the end of a piece continue with the piece that starts there, or
    /// walk the cell border clockwise to the nearest start of an unused piece (through the corners).
    /// </summary>
    private static List<List<(double X, double Y, int Cut)>> Assemble(List<Piece> records, (double U0, double V0, double U1, double V1) r)
    {
        var (u0, v0, u1, v1) = r;
        int n = records.Count;
        var visited = new bool[n];
        var polys = new List<List<(double X, double Y, int Cut)>>();
        int NextSide(int s) => s switch { 4 => 1, 1 => 8, 8 => 2, _ => 4 };
        (double X, double Y) CornerAfter(int s) => s switch { 4 => (u0, v0), 1 => (u0, v1), 8 => (u1, v1), _ => (u1, v0) };
        while (true)
        {
            int k = -1;
            for (int i = 0; i < n; i++) if (!visited[i]) { k = i; break; }
            if (k < 0) break;
            var poly = new List<(double X, double Y, int Cut)> { (records[k].A.X, records[k].A.Y, records[k].ACut ? 1 : 0) };
            var first = records[k].A;
            int cur = k;
            bool ok = true, closed = false;
            int guard = 0;
            while (true)
            {
                if (++guard > 100000) { ok = false; break; }
                visited[cur] = true;
                var ex = records[cur].B;
                if (Close(ex, first)) { closed = true; break; }
                if (!Close(ex, (poly[^1].X, poly[^1].Y))) poly.Add((ex.X, ex.Y, records[cur].BCut ? 1 : 0));
                int nxt = -1;
                for (int i = 0; i < n; i++) if (!visited[i] && Close(records[i].A, ex)) { nxt = i; break; }
                if (nxt >= 0) { cur = nxt; continue; }
                int side = SideOf(ex, r, true);
                if (side != 1 && side != 2 && side != 4 && side != 8) { ok = false; break; }
                var pt = ex;
                int found = -1;
                for (int rep = 0; rep < 5; rep++)
                {
                    double best = 1e300;
                    int bi = -1;
                    for (int i = 0; i < n; i++)
                    {
                        var cand = records[i].A;
                        if (visited[i] && !Close(cand, first)) continue;
                        if (SideOf(cand, r, false) != side) continue;
                        double d = side switch { 4 => pt.X - cand.X, 2 => pt.Y - cand.Y, 8 => cand.X - pt.X, _ => cand.Y - pt.Y };
                        if (d >= -Eps && best > d) { best = d; bi = i; }
                    }
                    if (bi >= 0) { found = bi; break; }
                    var corner = CornerAfter(side);
                    if (!Close(corner, (poly[^1].X, poly[^1].Y))) poly.Add((corner.X, corner.Y, 0));
                    pt = corner;
                    side = NextSide(side);
                }
                if (found < 0) { ok = false; break; }
                var c = records[found].A;
                if (Close(c, first)) { closed = true; break; }
                if (!Close(c, (poly[^1].X, poly[^1].Y))) poly.Add((c.X, c.Y, records[found].ACut ? 1 : 0));
                cur = found;
            }
            if (closed && ok && poly.Count >= 3) polys.Add(poly);
        }
        return polys;
    }

    /// <summary>Clockwise polygons of one leaf cell. Null when no loop edge touches the cell and its centre is inside the face (the
    /// cell is interior); an empty list when it is outside.</summary>
    internal static List<List<(double X, double Y, int Cut)>>? CellPolygons(Cell cell, List<List<(double X, double Y)>> loops,
        List<List<(double X, double Y)>> uvLoops)
    {
        var r = (cell.U0, cell.V0, cell.U1, cell.V1);
        var recs = new List<Piece>();
        foreach (var a in loops)
            foreach (var (p, q) in EdgeOrder(a))
                if (ClipSegment(p, q, r) is { } piece) recs.Add(piece);
        if (recs.Count == 0)
        {
            var centre = ((cell.U0 + cell.U1) / 2, (cell.V0 + cell.V1) / 2);
            return GeTess.InsideEvenOdd(centre, uvLoops.Select(l => (IReadOnlyList<(double X, double Y)>)l).ToList())
                ? null : new List<List<(double X, double Y, int Cut)>>();
        }
        return Assemble(recs, r);
    }
}
