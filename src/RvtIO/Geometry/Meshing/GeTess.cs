namespace RvtIO.Geometry.Meshing;

internal static class GeTess
{
    /// <summary>Tolerance argument of every call of the mesher: contour tolerance min(1e-10, 1e-9 * size).</summary>
    internal const double RendererTolerance = -1e-10;

    private const double Infinity = 1e300;

    private sealed class Vtx
    {
        public readonly int Idx;
        public Contour C;
        public Vtx Nxt = null!, Prv = null!;
        public Vtx(int idx, Contour c) { Idx = idx; C = c; }
    }

    private sealed class Contour
    {
        public readonly (double X, double Y)[] Pts;
        public Vtx? Head;
        public Contour? Next;
        public bool Cw;
        public int Flag; // bit 0: degenerate or ray tried, bit 1: a hole needed the retry
        public double Tol;
        public Contour((double X, double Y)[] pts) { Pts = pts; }

        public Vtx AddVertex(int idx)
        {
            var v = new Vtx(idx, this);
            if (Head is { } h)
            {
                h.Prv.Nxt = v; v.Prv = h.Prv; h.Prv = v; v.Nxt = h;
            }
            else
            {
                Head = v; v.Prv = v; v.Nxt = v;
            }
            return v;
        }

        public List<Vtx> Verts()
        {
            var list = new List<Vtx>();
            if (Head is not { } h) return list;
            var v = h;
            do { list.Add(v); v = v.Nxt; } while (v != h);
            return list;
        }

        public (double X, double Y) Pt(Vtx v) => Pts[v.Idx];
        public bool IsLine => Head is { } h && h.Nxt == h.Prv;
        public bool IsTriangle => Head is { } h && h.Nxt.Nxt == h.Prv;
    }

    private static (double X, double Y) P(Vtx v) => v.C.Pts[v.Idx];

    private static void Reverse(Contour c)
    {
        if (c.Head is { } head)
        {
            var v = head;
            do
            {
                var old = v.Nxt;
                v.Nxt = v.Prv; v.Prv = old;
                v = old;
            } while (v != head);
        }
        c.Cw = !c.Cw;
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private static double CalcMaxCoord(Contour c)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var v in c.Verts())
        {
            var p = c.Pt(v);
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        return Hypot(maxX - minX, maxY - minY);
    }

    private static double SquareTolerance(Contour c, double a2, bool flag)
    {
        var vs = c.Verts();
        double sx = 0, sy = 0, mx = 0, my = 0;
        var p0 = c.Pt(vs[0]);
        double minx = Math.Abs(p0.X), miny = Math.Abs(p0.Y);
        foreach (var v in vs)
        {
            var p = c.Pt(v);
            double ax = Math.Abs(p.X), ay = Math.Abs(p.Y);
            sx += ax; sy += ay;
            minx = Math.Min(minx, ax); miny = Math.Min(miny, ay);
            mx = Math.Max(ax, mx); my = Math.Max(ay, my);
        }
        double m = Math.Max(my, mx);
        int n = vs.Count;
        if (flag)
        {
            double k = (my - miny + mx - minx) * n;
            return Math.Max(m * 2.220446049250313e-16 * k * 1000.0, k * a2);
        }
        double vv = m == mx ? sy : sx;
        return Math.Max((m + m) * (vv * 2.220446049250313e-16), (sy + sx) * a2);
    }

    private static double AreaOf(Contour c, bool relative)
    {
        var vs = c.Verts();
        var first = c.Pt(vs[0]);
        double x0 = relative ? first.X : 0.0, y0 = relative ? first.Y : 0.0;
        double s = 0.0;
        var p = first;
        for (int k = 0; k < vs.Count; k++)
        {
            var q = c.Pt(vs[(k + 1) % vs.Count]);
            s += (p.Y - y0) * (q.X - x0) - (q.Y - y0) * (p.X - x0);
            p = q;
        }
        return s * 0.5;
    }

    private static void CheckClockwise(Contour c, double tolArg)
    {
        double v9 = c.Head != null ? AreaOf(c, false) : 0.0;
        double sq = c.Head != null ? SquareTolerance(c, tolArg, false) : 0.0;
        if (v9 > sq || -sq > v9) { c.Cw = v9 >= 0.0; return; }
        v9 = c.Head != null ? AreaOf(c, true) : 0.0;
        sq = c.Head != null ? SquareTolerance(c, tolArg, true) : 0.0;
        if (v9 > sq || -sq > v9) c.Cw = v9 >= 0.0;
        else c.Flag |= 1;
    }

    private static void RemoveEqualVertices(Contour? c, double tol)
    {
        for (; c != null; c = c.Next)
        {
            var v4 = c.Head;
            if (v4 == null || v4 == v4.Nxt) continue;
            while (true)
            {
                Vtx v5;
                bool found = false;
                while (true)
                {
                    v5 = v4.Nxt;
                    var p = c.Pt(v4); var q = c.Pt(v5);
                    if (Hypot(p.X - q.X, p.Y - q.Y) <= tol) { found = true; break; }
                    v4 = v5;
                    if (v5 == c.Head) break;
                }
                if (!found) break;
                if (v5 == c.Head)
                {
                    if (v4 == c.Head)
                    {
                        var n = v4.Nxt;
                        c.Head = n == v4 ? null : n;
                    }
                    var a0 = v4.Prv; var b0 = v4.Nxt;
                    a0.Nxt = b0; b0.Prv = a0;
                    break;
                }
                var a = v5.Prv; var b = v5.Nxt;
                a.Nxt = b; b.Prv = a;
            }
        }
    }

    private static bool IsIncoming(Vtx a, Vtx b)
    {
        var pa = P(a); var pp = P(a.Prv); var pn = P(a.Nxt); var pb = P(b);
        double ix = pa.X - pp.X, iy = pa.Y - pp.Y;
        double ox = pa.X - pn.X, oy = pa.Y - pn.Y;
        double wx = pa.X - pb.X, wy = pa.Y - pb.Y;
        double v19 = wy * ix, v20 = wx * iy;
        if (oy * ix - ox * iy >= 0.0)
        {
            if (v19 - v20 <= 0.0 || wx * oy - wy * ox <= 0.0) return false;
        }
        else if (v20 - v19 >= 0.0 && wy * ox - wx * oy >= 0.0) return false;
        return true;
    }

    private static bool InRange(double t, double lenSq, double tolSq) =>
        t < 0.0 ? tolSq >= lenSq * t * t : t < 1.0 && tolSq < (1.0 - t) * lenSq * (1.0 - t);

    private static (bool Hit, double T) SegHit((double X, double Y) a, (double X, double Y) b, double lenSq,
        (double X, double Y) c, (double X, double Y) d, double tolSq)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double acx = c.X - a.X, acy = c.Y - a.Y;
        double cr = acy * abx - acx * aby;
        if (cr * cr <= tolSq)
        {
            double t0 = (acy * aby + acx * abx) / lenSq;
            return (InRange(t0, lenSq, tolSq), t0);
        }
        double cdx = d.X - c.X, cdy = d.Y - c.Y;
        double den = cdx * aby - cdy * abx;
        if (den * den <= tolSq) return (false, 0.0);
        double inv = 1.0 / den;
        double s = inv * cr;
        double lenCd = cdy * cdy + cdx * cdx;
        if (s >= 0.0)
        {
            if (s >= 1.0 || tolSq >= (1.0 - s) * lenCd * (1.0 - s)) return (false, 0.0);
        }
        else if (tolSq < lenCd * s * s) return (false, 0.0);
        double t = (cdx * acy - cdy * acx) * inv;
        return (InRange(t, lenSq, tolSq), t);
    }

    private static List<(Vtx V, double T)> StableSortByT(List<(Vtx V, double T)> list)
    {
        var o = new List<(Vtx V, double T)>(list);
        for (int i = 1; i < o.Count; i++)
        {
            var x = o[i];
            int j = i - 1;
            while (j >= 0 && o[j].T > x.T) { o[j + 1] = o[j]; j--; }
            o[j + 1] = x;
        }
        return o;
    }

    /// <summary>Edges of the contour chain hit by the segment a-b (the ray of a vertex, or the chord to a candidate), sorted by
    /// the parameter on the segment. Edges that touch the vertex or the second end (by point index) are skipped.</summary>
    private static List<(Vtx V, double T)> Collect(Contour chain, int skipA, (double X, double Y) a, (double X, double Y) b,
        int skipB, double tol, bool allContours)
    {
        double lenSq = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
        double tolSq = tol * tol;
        double minx = Math.Min(a.X, b.X), maxx = Math.Max(a.X, b.X), miny = Math.Min(a.Y, b.Y), maxy = Math.Max(a.Y, b.Y);
        var output = new List<(Vtx V, double T)>();
        for (Contour? c = chain; c != null; c = c.Next)
        {
            if (c.Head != null)
            {
                foreach (var v1 in c.Verts())
                {
                    var v2 = v1.Nxt;
                    if (v1.Idx == skipA || v2.Idx == skipA || v1.Idx == skipB || v2.Idx == skipB) continue;
                    var cc = P(v1); var dd = P(v2);
                    if ((minx - tol <= cc.X || minx - tol <= dd.X) && (cc.X <= tol + maxx || dd.X <= tol + maxx)
                        && (miny - tol <= cc.Y || miny - tol <= dd.Y) && (cc.Y <= tol + maxy || dd.Y <= tol + maxy))
                    {
                        var (hit, t) = SegHit(a, b, lenSq, cc, dd, tolSq);
                        if (hit) output.Add((v1, t));
                    }
                }
            }
            if (!allContours) break;
        }
        return StableSortByT(output);
    }

    private static Vtx? ChooseTarget(Contour root, Vtx v, List<(Vtx V, double T)> list, bool allContours)
    {
        double best = Infinity;
        double tol = root.Tol;
        while (true)
        {
            if (list.Count == 0) return null;
            var e0 = list[0].V;
            var e1 = e0.Nxt;
            if (allContours && e0.C == v.C) return null;
            Vtx? cand = null;
            double d0 = best, dist;
            if (e0.Prv != v && IsIncoming(e0, v) && IsIncoming(v, e0))
            {
                var p = P(e0); var q = P(v);
                d0 = (p.Y - q.Y) * (p.Y - q.Y) + (p.X - q.X) * (p.X - q.X);
                cand = e0;
            }
            if (e1.Nxt != v && IsIncoming(e1, v) && IsIncoming(v, e1))
            {
                var p = P(e1); var q = P(v);
                double d1 = (p.Y - q.Y) * (p.Y - q.Y) + (p.X - q.X) * (p.X - q.X);
                if (cand != null)
                {
                    if (d1 <= d0) { cand = e1; dist = d1; } else dist = d0;
                }
                else { cand = e1; dist = d1; }
            }
            else if (cand != null) dist = d0;
            else
            {
                if (list.Count <= 1) return null;
                var second = list[1].V;
                if (list[0].V.Idx != second.Nxt.Idx || list[0].V.Nxt.Idx != second.Idx) return null;
                list = list.GetRange(1, list.Count - 1);
                continue;
            }
            if (dist >= best || (dist <= tol && -tol <= dist)) return null;
            best = dist;
            var next = Collect(root, v.Idx, P(v), P(cand!), cand!.Idx, tol, allContours);
            if (next.Count == 0) return cand;
            list = next;
        }
    }

    /// <summary>c keeps [v..w], a new contour [w..v] is inserted behind it.</summary>
    private static void SplitPolygon(Contour c, Vtx v, Vtx w)
    {
        var n = new Contour(c.Pts) { Cw = c.Cw };
        c.Head = v; c.AddVertex(v.Idx);
        c.Head = w; c.AddVertex(w.Idx);
        var v10 = w.Prv; var v11 = v.Prv;
        v10.Nxt = v; v.Prv = v10;
        v11.Nxt = w; w.Prv = v11;
        n.Head = w;
        n.Next = c.Next;
        c.Next = n;
        c.Head = v;
        Relabel(n); Relabel(c);
    }

    private static void Relabel(Contour c)
    {
        foreach (var v in c.Verts()) v.C = c;
    }

    private static void ConcatContours(Contour self, Vtx a2, Contour other, Vtx a4)
    {
        self.Head = a2; self.AddVertex(a2.Idx);
        other.Head = a4; other.AddVertex(a4.Idx);
        var v7 = self.Head; var v8 = other.Head;
        var v9 = v7.Prv; var v10 = v8.Prv;
        v9.Nxt = v8; v8.Prv = v9;
        v10.Nxt = v7; v7.Prv = v10;
        Relabel(self);
        other.Head = null;
    }

    /// <summary>The ray test shared by normalize and breakToConvex2: a reflex vertex (turn below 0.1) shoots a ray of length v4.</summary>
    private static bool RayFor(Vtx v, double tol, double v4, out (double X, double Y) r)
    {
        r = default;
        var p = P(v); var pr = P(v.Prv); var nx = P(v.Nxt);
        double ay = p.Y - pr.Y, ax = p.X - pr.X;
        double la2 = ay * ay + ax * ax;
        double tol2 = tol * tol;
        if (!(la2 > tol2 || -tol2 > la2)) return false;
        double by = p.Y - nx.Y, bx = p.X - nx.X;
        double lb2 = by * by + bx * bx;
        if (!(lb2 > tol2 || -tol2 > lb2)) return false;
        double v24 = Math.Sqrt(lb2 * la2);
        double v25 = (by * ax - ay * bx) / v24;
        if (!(v25 <= 0.1 && (v25 <= 0.0 || by * ay + bx * ax <= 0.0))) return false;
        if (v25 >= -0.2)
        {
            double la = Math.Sqrt(la2);
            double v29 = -v4 / la;
            r = (p.X - ay * v29, v29 * ax + p.Y);
        }
        else
        {
            double dx = bx + ax, dy = by + ay;
            double ln = Hypot(dx, dy);
            if (ln > tol) { dx /= ln; dy /= ln; }
            r = (v4 * dx + p.X, v4 * dy + p.Y);
        }
        return true;
    }

    private static void BreakToConvex2(Contour root, double maxc)
    {
        double v4 = maxc * 1.1;
        for (Contour? c = root; c != null; c = c.Next)
        {
            c.Tol = root.Tol;
            while (!c.IsLine && !c.IsTriangle)
            {
                var v6 = c.Head;
                Vtx? w = null;
                if (v6 != null)
                {
                    while (true)
                    {
                        if (RayFor(v6, root.Tol, v4, out var r))
                        {
                            c.Flag |= 1;
                            var lst = Collect(c, v6.Idx, P(v6), r, -1, root.Tol, false);
                            if (lst.Count > 0)
                            {
                                w = ChooseTarget(c, v6, lst, false);
                                if (w != null) break;
                            }
                        }
                        v6 = v6.Nxt;
                        if (v6 == c.Head) break;
                    }
                }
                if (w != null)
                {
                    c.Flag &= ~1;
                    SplitPolygon(c, v6!, w);
                    continue;
                }
                if ((c.Flag & 1) == 0 || !SplitOnCoincidentPoint(c)) break;
            }
        }
    }

    private static (double T, double Dist) SegParam((double X, double Y) a, (double X, double Y) b, (double X, double Y) p)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double l2 = abx * abx + aby * aby;
        if (l2 == 0.0) return (0.0, Hypot(p.X - a.X, p.Y - a.Y));
        double t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / l2;
        double tc = Math.Min(1.0, Math.Max(0.0, t));
        double qx = a.X + abx * tc, qy = a.Y + aby * tc;
        return (t, Hypot(p.X - qx, p.Y - qy));
    }

    /// <summary>A vertex that lies on a non adjacent edge pinches the contour: it is cut into two contours at that point.</summary>
    private static bool SplitOnCoincidentPoint(Contour c)
    {
        var v3 = c.Head!;
        do
        {
            var v4 = v3.Nxt.Nxt;
            var v5 = v4.Nxt;
            var v6 = v3.Prv;
            var p = P(v3);
            while (true)
            {
                var (t, dist) = SegParam(P(v4), P(v5), p);
                if (dist <= 1e-10)
                {
                    c.Flag &= ~1;
                    if (Math.Abs(t - 1.0) > 1e-10)
                    {
                        c.Head = v5;
                        v5 = c.AddVertex(v3.Idx);
                    }
                    var n = new Contour(c.Pts) { Cw = c.Cw };
                    var a = v3.Prv;
                    c.Head = a;
                    a.Nxt = v5; v5.Prv = a;
                    v4.Nxt = v3; v3.Prv = v4;
                    n.Head = v3;
                    n.Next = c.Next;
                    c.Next = n;
                    Relabel(n); Relabel(c);
                    return true;
                }
                v4 = v5;
                v5 = v5.Nxt;
                if (v4 == v6) break;
            }
            v3 = v3.Nxt;
        } while (v3 != c.Head);
        return false;
    }

    /// <summary>Removes equal vertices, fixes the orientation (outer clockwise, holes counter-clockwise) and merges the holes
    /// into the outer contour by bridges. False when the outer contour has no area.</summary>
    private static bool Normalize(Contour root, double maxc, double tolArg)
    {
        double v4 = maxc * 1.1;
        RemoveEqualVertices(root, root.Tol);
        CheckClockwise(root, tolArg);
        if ((root.Flag & 1) != 0) return false;
        if (!root.Cw) Reverse(root);
        for (var h = root.Next; h != null; h = h.Next)
        {
            CheckClockwise(h, tolArg);
            if (h.Cw) Reverse(h);
        }
        var hole = root.Next;
        while (hole != null)
        {
            bool flipped = false;
            var v = hole.Head;
            if (v == null || v.Nxt == v.Prv)
            {
                hole = hole.Next;
                root.Next = hole;
                continue;
            }
            Vtx? w = null;
            while (true)
            {
                var lst = new List<(Vtx V, double T)>();
                if (RayFor(v, root.Tol, v4, out var r))
                {
                    lst = Collect(root, v.Idx, P(v), r, -1, root.Tol, true);
                    if (lst.Count > 0 && lst[0].V.C != hole)
                    {
                        w = ChooseTarget(root, v, lst, true);
                        if (w != null) break;
                    }
                }
                v = v.Nxt;
                if (v == hole.Head)
                {
                    root.Flag |= 2;
                    int cnt = 1;
                    for (int k = 1; k < lst.Count; k++) if (lst[k].V.C == hole) cnt++;
                    if ((cnt & 1) == 0 || flipped)
                    {
                        hole = hole.Next;
                        root.Next = hole;
                        w = null;
                        break;
                    }
                    Reverse(hole);
                    flipped = true;
                    v = hole.Head!.Nxt;
                }
            }
            if (w == null) continue;
            var target = w.C;
            if (target == root)
            {
                ConcatContours(root, w, hole!, v);
                hole = hole!.Next;
                root.Next = hole;
            }
            else
            {
                ConcatContours(hole!, v, target, w);
                var i = hole!.Next;
                var prev = hole;
                while (i != target) { prev = i!; i = i!.Next; }
                prev.Next = target.Next;
                target.Next = null;
                hole = root.Next;
            }
        }
        return true;
    }

    internal static List<int[]> ConvexPieces((double X, double Y)[] pts, IReadOnlyList<int[]> loops, double tolArg = RendererTolerance) =>
        ConvexPieces(pts, loops, out _, tolArg);

    /// <summary>
    /// <paramref name="ok"/> is false when a contour could not be decomposed (no area, or a reflex vertex without a partner);
    /// its pieces are returned as they are. The mesher accepts that for a lone polygon and refuses it when merging seams.
    /// </summary>
    internal static List<int[]> ConvexPieces((double X, double Y)[] pts, IReadOnlyList<int[]> loops, out bool ok, double tolArg = RendererTolerance) =>
        ConvexPiecesCore(pts, loops, out ok, 1, tolArg);

    /// <summary>
    /// As <c>ConvexPieces</c> with <paramref name="ok"/> false also for a contour that needed the hole retry (flag bit 1): the
    /// state in which the mesher takes a fallback the freeform track does not reproduce.
    /// </summary>
    internal static List<int[]> ConvexPiecesStrict((double X, double Y)[] pts, IReadOnlyList<int[]> loops, out bool ok, double tolArg = RendererTolerance) =>
        ConvexPiecesCore(pts, loops, out ok, 3, tolArg);

    private static List<int[]> ConvexPiecesCore((double X, double Y)[] pts, IReadOnlyList<int[]> loops, out bool ok, int flagMask, double tolArg)
    {
        ok = true;
        Contour? root = null, prev = null;
        foreach (var lp in loops)
        {
            var c = new Contour(pts);
            foreach (int i in lp) c.AddVertex(i);
            if (root == null) root = c; else prev!.Next = c;
            prev = c;
        }
        var pieces = new List<int[]>();
        if (root == null) return pieces;
        double maxc = CalcMaxCoord(root);
        root.Tol = tolArg >= 0.0 ? (tolArg <= 0.0 ? 1e-9 : tolArg) * maxc : Math.Min(-tolArg, maxc * 1e-9);
        if (Normalize(root, maxc, tolArg)) BreakToConvex2(root, maxc);
        for (var c = root; c != null; c = c.Next)
        {
            if ((c.Flag & flagMask) != 0) ok = false;
            var vs = c.Verts();
            if (vs.Count >= 3) pieces.Add(vs.Select(v => v.Idx).ToArray());
        }
        return pieces;
    }

    internal static double SignedArea(IReadOnlyList<(double X, double Y)> poly)
    {
        double s = 0.0;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2;
    }

    internal static bool InsideEvenOdd((double X, double Y) pt, IReadOnlyList<IReadOnlyList<(double X, double Y)>> loops)
    {
        bool c = false;
        foreach (var poly in loops)
        {
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % n];
                if ((a.Y > pt.Y) != (b.Y > pt.Y))
                {
                    double xx = a.X + (pt.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    if (xx > pt.X) c = !c;
                }
            }
        }
        return c;
    }

    private static (double X, double Y) InteriorPoint(IReadOnlyList<(double X, double Y)> poly)
    {
        double a = SignedArea(poly);
        int n = poly.Count;
        if (Math.Abs(a) > 1e-300)
        {
            double cx = 0, cy = 0;
            for (int i = 0; i < n; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % n];
                double cr = p.X * q.Y - q.X * p.Y;
                cx += (p.X + q.X) * cr; cy += (p.Y + q.Y) * cr;
            }
            var c = (X: cx / (6 * a), Y: cy / (6 * a));
            if (InsideEvenOdd(c, new IReadOnlyList<(double X, double Y)>[] { poly })) return c;
        }
        return (poly.Sum(p => p.X) / n, poly.Sum(p => p.Y) / n);
    }

    /// <summary>
    /// The sign of a loop count tells outer from hole; the mesher derives it from the loop orientation (counter-clockwise
    /// outer, clockwise hole). A hole goes with the smallest outer loop that contains it.
    /// </summary>
    private static List<(int Outer, List<int> Holes)> ClassifyProfile(List<List<(double X, double Y)>> polys, bool[]? outerFlags)
    {
        outerFlags ??= polys.Select(p => SignedArea(p) > 0).ToArray();
        var outers = Enumerable.Range(0, polys.Count).Where(i => outerFlags[i]).ToList();
        var groups = outers.ToDictionary(i => i, _ => new List<int>());
        for (int h = 0; h < polys.Count; h++)
        {
            if (outerFlags[h]) continue;
            int best = -1;
            // the outer loop a hole lies in holds most of the hole's own points (the centre of a hole can lie in an island inside it)
            foreach (int o in outers)
                if (polys[h].Count(p => InsideEvenOdd(p, new IReadOnlyList<(double X, double Y)>[] { polys[o] })) * 2 > polys[h].Count
                    && (best < 0 || Math.Abs(SignedArea(polys[o])) < Math.Abs(SignedArea(polys[best])))) best = o;
            if (best >= 0) groups[best].Add(h);
        }
        return outers.Select(o => (o, groups[o])).ToList();
    }

    internal static List<(int A, int B, int C)> TriangulateProfile(IReadOnlyList<(double X, double Y)> pts,
        IReadOnlyList<int[]> loops, bool[]? outerFlags = null)
    {
        var flat = loops.SelectMany(l => l).ToArray();
        var coords = flat.Select(i => pts[i]).ToArray();
        var local = new List<int[]>();
        int k = 0;
        foreach (var lp in loops) { local.Add(Enumerable.Range(k, lp.Length).ToArray()); k += lp.Length; }
        var polys = loops.Select(lp => lp.Select(i => pts[i]).ToList()).ToList();
        var tris = new List<(int A, int B, int C)>();
        foreach (var (outer, holes) in ClassifyProfile(polys, outerFlags))
        {
            var region = new List<int[]> { local[outer] };
            region.AddRange(holes.Select(h => local[h]));
            foreach (var piece in ConvexPieces(coords, region))
                for (int j = 1; j < piece.Length - 1; j++)
                    tris.Add((flat[piece[0]], flat[piece[j]], flat[piece[j + 1]]));
        }
        return tris;
    }
}
