namespace RvtIO.Geometry.Meshing;

using Cell = RevolvedGrid.Cell;
using Pt = (double U, double V);

/// <summary>A face the revolved rule does not cover (the mesher turns it into a refusal).</summary>
internal sealed class RevolvedUnsupportedException : Exception
{
    public RevolvedUnsupportedException(string message) : base(message) { }
}

/// <summary>
/// From the grid and the (counter-clockwise) uv loop to the polygons handed to the profile triangulation: grid T-vertices,
/// clipping of the loop against the leaf cells, chaining of the clipped pieces into rings.
/// </summary>
internal static class RevolvedCells
{
    private const double TolUv = 1e-10;

    /// <summary>A leaf cell of the work list: a plain quad (two triangles) or a ring for the profile triangulation.</summary>
    internal sealed record Item(Cell Cell, bool IsQuad, List<Pt>? Ring, List<List<Pt>>? Holes = null);

    private static bool Eq(Pt a, Pt b) => Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V)) <= TolUv;

    // side code of a point: 1 left, 2 right, 4 bottom, 8 top; corners are remapped for the current and the candidate side
    private static int SideCode(Pt p, double[] r, bool candidate)
    {
        int c = 0;
        if (Math.Abs(p.U - r[0]) <= TolUv) c = 1;
        else if (Math.Abs(p.U - r[2]) <= TolUv) c = 2;
        if (Math.Abs(p.V - r[1]) <= TolUv) c |= 4;
        else if (Math.Abs(p.V - r[3]) <= TolUv) c |= 8;
        return c switch
        {
            9 => candidate ? 1 : 8,
            10 => candidate ? 8 : 2,
            6 => candidate ? 2 : 4,
            5 => candidate ? 4 : 1,
            _ => c,
        };
    }

    private static (long, long) VKey(Pt p) => ((long)Math.Round(p.U / TolUv), (long)Math.Round(p.V / TolUv));

    /// <summary>Chains of one cell (start, end in registration order) to closed clockwise rings.</summary>
    private static List<List<Pt>> WalkPolygons(double[] r, List<(Pt S, Pt E)> recs)
    {
        int n = recs.Count;
        var visited = new bool[n];
        var startMap = new Dictionary<(long, long), int>();
        for (int i = 0; i < n; i++) startMap[VKey(recs[i].S)] = i; // the last registered chain wins
        var polys = new List<List<Pt>>();
        while (true)
        {
            int k = -1;
            for (int i = 0; i < n; i++) if (!visited[i]) { k = i; break; }
            if (k < 0) break;
            var ring = new List<Pt> { recs[k].S };
            bool? ok = true;
            while (true)
            {
                if (visited[k]) { ok = false; break; }
                visited[k] = true;
                var e = recs[k].E;
                if (Eq(e, ring[0])) break;
                ring.Add(e);
                int nxt = startMap.TryGetValue(VKey(e), out int found) ? found : -1;
                if (nxt >= 0 && !visited[nxt]) { k = nxt; continue; }
                if (nxt >= 0 && visited[nxt])
                {
                    int alt = -1;
                    for (int j = 0; j < n; j++) if (!visited[j] && Eq(recs[j].S, e)) { alt = j; break; }
                    if (alt >= 0) { k = alt; continue; }
                }
                // the chain ends on the cell border: walk along the border to the next chain start
                int side = SideCode(e, r, false);
                if (side == 0) { ring.RemoveAt(0); ok = null; break; }
                var cur = e;
                bool closed = false, exhausted = true;
                for (int it = 0; it < 5; it++)
                {
                    int best = -1;
                    double bd = 1e300;
                    for (int j = 0; j < n; j++)
                    {
                        if (visited[j] && !Eq(recs[j].S, ring[0])) continue;
                        var c = recs[j].S;
                        if (SideCode(c, r, true) != side) continue;
                        double d = side switch
                        {
                            1 => c.V - cur.V,
                            8 => c.U - cur.U,
                            2 => cur.V - c.V,
                            _ => cur.U - c.U,
                        };
                        if (d >= 0.0 && bd > d) { bd = d; best = j; }
                    }
                    if (best >= 0)
                    {
                        var c = recs[best].S;
                        if (Eq(c, ring[0])) { closed = true; exhausted = false; break; }
                        if (!Eq(ring[^1], c)) ring.Add(c);
                        k = best; exhausted = false;
                        break;
                    }
                    Pt corner;
                    int nside;
                    if (side == 4) { corner = (r[0], r[1]); nside = 1; }
                    else if (side == 2) { corner = (r[2], r[1]); nside = 4; }
                    else if (side == 8) { corner = (r[2], r[3]); nside = 2; }
                    else { corner = (r[0], r[3]); nside = 8; }
                    if (!Eq(ring[^1], corner))
                    {
                        if (Eq(corner, ring[0])) { closed = true; exhausted = false; break; }
                        ring.Add(corner);
                    }
                    side = nside;
                    cur = corner;
                }
                if (exhausted) { ring.RemoveAt(0); ok = null; break; }
                if (closed) break;
            }
            if (ok != false && ring.Count >= 3) polys.Add(ring);
        }
        return polys;
    }

    /// <summary>Every segment of the loop is parallel to an axis (the loop is the border of a rectangle of the parameter plane or a union of such).</summary>
    internal static bool IsAxisParallel(IReadOnlyList<Pt> loop)
    {
        for (int i = 0; i + 1 < loop.Count; i++)
            if (!(Math.Abs(loop[i].V - loop[i + 1].V) <= TolUv || Math.Abs(loop[i].U - loop[i + 1].U) <= TolUv)) return false;
        return true;
    }

    /// <summary>Pieces of an oblique segment q0 to q1 inside the leaf cells (clipped to their rectangles), in travel order.</summary>
    private static List<(Cell L, Pt P0, Pt P1)> ClipObliqueSegment(Pt q0, Pt q1, List<Cell> leaves)
    {
        var res = new List<(double T, Cell L, Pt P0, Pt P1)>();
        double dx = q1.U - q0.U, dy = q1.V - q0.V;
        foreach (var l in leaves)
        {
            double t0 = 0.0, t1 = 1.0;
            bool Clip(double p, double q)
            {
                if (Math.Abs(p) <= 1e-300) return q >= -TolUv;
                double r = q / p;
                if (p < 0.0) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
                return true;
            }
            if (!(Clip(-dx, q0.U - l.U0) && Clip(dx, l.U1 - q0.U) && Clip(-dy, q0.V - l.V0) && Clip(dy, l.V1 - q0.V))) continue;
            if (t1 - t0 <= 0.0) continue;
            Pt Snapped(double t)
            {
                double u = q0.U + t * dx, v = q0.V + t * dy;
                if (Math.Abs(u - l.U0) <= TolUv) u = l.U0; else if (Math.Abs(u - l.U1) <= TolUv) u = l.U1;
                if (Math.Abs(v - l.V0) <= TolUv) v = l.V0; else if (Math.Abs(v - l.V1) <= TolUv) v = l.V1;
                return (u, v);
            }
            Pt p0 = Snapped(t0), p1 = Snapped(t1);
            if (Eq(p0, p1)) continue;
            res.Add((t0, l, p0, p1));
        }
        return res.OrderBy(x => x.T).Select(x => (x.L, x.P0, x.P1)).ToList();
    }

    /// <summary>Pieces of an axis parallel segment q0 to q1 that lie on the border of leaf cells, in travel order.</summary>
    private static List<(Cell L, Pt P0, Pt P1)> ClipAxisSegment(Pt q0, Pt q1, List<Cell> leaves)
    {
        var res = new List<(double T, Cell L, Pt P0, Pt P1)>();
        if (Eq(q0, q1)) return new();
        bool horiz = Math.Abs(q0.V - q1.V) <= TolUv, vert = Math.Abs(q0.U - q1.U) <= TolUv;
        if (!(horiz || vert)) return ClipObliqueSegment(q0, q1, leaves);
        foreach (var l in leaves)
        {
            if (horiz)
            {
                double c = q0.V;
                if (!(Math.Abs(l.V0 - c) <= TolUv || Math.Abs(l.V1 - c) <= TolUv)) continue;
                double lo = Math.Min(q0.U, q1.U), hi = Math.Max(q0.U, q1.U);
                double a = Math.Max(lo, l.U0), b = Math.Min(hi, l.U1);
                if (Math.Abs(a - l.U0) <= TolUv) a = l.U0;
                if (Math.Abs(b - l.U1) <= TolUv) b = l.U1;
                if (b - a <= TolUv) continue;
                Pt p0 = q1.U >= q0.U ? (a, c) : (b, c), p1 = q1.U >= q0.U ? (b, c) : (a, c);
                res.Add((Math.Abs(p0.U - q0.U), l, p0, p1));
            }
            else
            {
                double c = q0.U;
                if (!(Math.Abs(l.U0 - c) <= TolUv || Math.Abs(l.U1 - c) <= TolUv)) continue;
                double lo = Math.Min(q0.V, q1.V), hi = Math.Max(q0.V, q1.V);
                double a = Math.Max(lo, l.V0), b = Math.Min(hi, l.V1);
                if (Math.Abs(a - l.V0) <= TolUv) a = l.V0;
                if (Math.Abs(b - l.V1) <= TolUv) b = l.V1;
                if (b - a <= TolUv) continue;
                Pt p0 = q1.V >= q0.V ? (c, a) : (c, b), p1 = q1.V >= q0.V ? (c, b) : (c, a);
                res.Add((Math.Abs(p0.V - q0.V), l, p0, p1));
            }
        }
        return res.OrderBy(x => x.T).Select(x => (x.L, x.P0, x.P1)).ToList();
    }

    /// <summary>Pieces that run along a cell edge in the counter-clockwise direction are not registered.</summary>
    private static bool PieceRegistered(Pt p0, Pt p1, double x0, double y0, double x1, double y1)
    {
        if (Eq(p0, p1)) return false;
        double dx = p1.U - p0.U, dy = p1.V - p0.V;
        if (Math.Abs(dx) <= TolUv)
        {
            if (Math.Abs(p0.U - x0) <= TolUv && p0.V > p1.V) return false;
            if (Math.Abs(p0.U - x1) <= TolUv && p1.V > p0.V) return false;
        }
        if (Math.Abs(dy) <= TolUv)
        {
            if (Math.Abs(p0.V - y0) <= TolUv && p1.U > p0.U) return false;
            if (Math.Abs(p0.V - y1) <= TolUv && p0.U > p1.U) return false;
        }
        return true;
    }

    /// <summary>The end points of every split line become T-vertices of the leaf cells that contain them on an edge (not on a
    /// corner); the subtree of the split cell itself is skipped.</summary>
    private static void DistributeTVertices(List<Cell> cells)
    {
        foreach (var c in cells) c.T = new();
        for (int i = 1; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c.Dir < 0) continue;
            var pts = c.Dir == 1 ? new Pt[] { (c.U1, c.Val), (c.U0, c.Val) } : new Pt[] { (c.Val, c.V1), (c.Val, c.V0) };
            foreach (var p in pts) Descend(cells, p, c.I, 0);
        }
    }

    private static void Descend(List<Cell> cells, Pt p, int skip, int ci)
    {
        var c = cells[ci];
        if (ci == skip) return;
        if (c.Ch0 >= 0)
        {
            foreach (int ch in new[] { c.Ch0, c.Ch1 })
            {
                var cc = cells[ch];
                if (cc.U0 - 1e-10 <= p.U && p.U <= cc.U1 + 1e-10 && cc.V0 - 1e-10 <= p.V && p.V <= cc.V1 + 1e-10)
                    Descend(cells, p, skip, ch);
            }
            return;
        }
        bool onv = Math.Abs(p.U - c.U0) <= 1e-10 || Math.Abs(p.U - c.U1) <= 1e-10;
        bool onh = Math.Abs(p.V - c.V0) <= 1e-10 || Math.Abs(p.V - c.V1) <= 1e-10;
        if ((onv && !onh) || (onh && !onv)) c.T.Add(p);
    }

    /// <summary>Grid T-vertices that lie inside a ring edge are inserted, sorted along the edge.</summary>
    private static List<Pt> InsertTVertices(List<Pt> ring, List<Pt> t)
    {
        if (t.Count == 0) return ring;
        var o = new List<Pt>();
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % n];
            o.Add(a);
            double dx = b.U - a.U, dy = b.V - a.V, ln2 = dx * dx + dy * dy;
            var ins = new List<(double T, Pt P)>();
            foreach (var p in t)
            {
                double cr = dx * (p.V - a.V) - dy * (p.U - a.U);
                if (cr * cr > 1e-20 * Math.Max(ln2, 1.0) && Math.Abs(cr) > 1e-10) continue;
                double tt = ln2 > 0 ? ((p.U - a.U) * dx + (p.V - a.V) * dy) / ln2 : 0.0;
                if (tt < 0 || tt > 1.0 + 1e-10) continue;
                if (Math.Abs(tt) <= 1e-10 || Math.Abs(tt - 1.0) <= 1e-10) continue;
                ins.Add((tt, p));
            }
            o.AddRange(ins.OrderBy(x => x.T).Select(x => x.P));
        }
        return o;
    }

    /// <summary>Items per leaf cell in cell index order. Each of <paramref name="loops"/> is closed (last = first) and counter-clockwise.</summary>
    internal static List<Item> CellWork(List<Cell> cells, List<List<Pt>> loops)
    {
        var leaves = cells.Where(c => c.Dir < 0).ToList();
        if (cells.Count == 1)
            return new() { new Item(cells[0], false, loops[0].Take(loops[0].Count - 1).ToList(),
                loops.Count > 1 ? loops.Skip(1).Select(h => h.Take(h.Count - 1).ToList()).ToList() : null) };
        DistributeTVertices(cells);
        foreach (var l in leaves) l.Recs = new();
        foreach (var loop in loops)
            for (int i = 0; i < loop.Count - 1; i++)
            {
                var q0 = loop[i + 1]; var q1 = loop[i];
                foreach (var (l, p0, p1) in ClipAxisSegment(q0, q1, leaves))
                    if (PieceRegistered(p0, p1, l.U0, l.V0, l.U1, l.V1)) l.Recs.Add((p0, p1));
            }
        var items = new List<Item>();
        var loopRegion = loops.Select(lp => (IReadOnlyList<(double X, double Y)>)lp.Take(lp.Count - 1).Select(p => (p.U, p.V)).ToList()).ToArray();
        foreach (var l in leaves.OrderBy(c => c.I))
        {
            double[] r = { l.U0, l.V0, l.U1, l.V1 };
            if (l.Recs.Count > 0)
            {
                var rings = new List<List<Pt>>();
                foreach (var ring in WalkPolygons(r, l.Recs.Select(x => (x.A, x.B)).ToList()))
                {
                    var rr = InsertTVertices(ring, l.T);
                    rr.Reverse();
                    rings.Add(rr);
                }
                double Signed(List<Pt> rg) { double s = 0; for (int i = 0; i < rg.Count; i++) { var a = rg[i]; var b = rg[(i + 1) % rg.Count]; s += a.U * b.V - b.U * a.V; } return s; }
                var big = rings.OrderByDescending(rg => Math.Abs(Signed(rg))).FirstOrDefault();
                var holes = big == null ? new List<List<Pt>>() : rings.Where(rg => rg != big && Math.Sign(Signed(rg)) != Math.Sign(Signed(big))).ToList();
                if (holes.Count == 0) foreach (var rg in rings) items.Add(new Item(l, false, rg));
                else
                {
                    // a hole polygon inside the leaf: the profile triangulation takes the outer ring with its holes
                    var outers = rings.Where(rg => !holes.Contains(rg)).ToList();
                    if (outers.Count != 1) throw new RevolvedUnsupportedException("hole among several polygons of a leaf");
                    items.Add(new Item(l, false, outers[0], holes));
                }
            }
            else
            {
                // a cell the loop does not touch is either wholly inside or wholly outside the face
                if (!GeTess.InsideEvenOdd(((l.U0 + l.U1) * 0.5, (l.V0 + l.V1) * 0.5), loopRegion))
                    continue;
                if (l.T.Count == 0) items.Add(new Item(l, true, null));
                else
                {
                    var ring = InsertTVertices(new List<Pt> { (l.U0, l.V0), (l.U0, l.V1), (l.U1, l.V1), (l.U1, l.V0) }, l.T);
                    ring.Reverse();
                    items.Add(new Item(l, false, ring));
                }
            }
        }
        return items;
    }
}
