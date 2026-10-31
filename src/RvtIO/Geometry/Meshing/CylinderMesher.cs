namespace RvtIO.Geometry.Meshing;

internal static class CylinderMesher
{
    private const double TwoPi = 2.0 * Math.PI;
    private const double Eps = KdTree.Eps;

    internal readonly record struct VEntry(double X, double Y, bool Removable, double[]? Pos3);

    /// <summary>Surface tolerance and maximum edge length come from the level of detail (Tolerances).</summary>
    internal static bool TryMesh(BrFace face, double surfaceTolerance, double maxEdgeLength, out Mesh mesh)
    {
        mesh = null!;
        if (face.Cylinder is not { } cyl || face.Loops.Count == 0 || !(surfaceTolerance > 0.0) || !(maxEdgeLength > 0.0)) return false;
        if (!Valid3(cyl.Origin) || !Valid3(cyl.Axis) || !Valid3(cyl.Ref) || !double.IsFinite(cyl.Radius) || cyl.Radius <= 0.0
            || !double.IsFinite(cyl.UScale) || cyl.UScale == 0.0 || !double.IsFinite(cyl.AngleLo) || !double.IsFinite(cyl.AngleHi)
            || !double.IsFinite(cyl.HeightLo) || !double.IsFinite(cyl.HeightHi)) return false;
        foreach (var loop in face.Loops)
            if (loop.Coedges.Count == 0) return false;
        try
        {
            var result = Run(face, cyl, surfaceTolerance, maxEdgeLength);
            if (result.Vertices.Any(v => !double.IsFinite(v[0]) || !double.IsFinite(v[1]) || !double.IsFinite(v[2]))) return false;
            mesh = result;
            return true;
        }
        catch (Exception ex) when (ex is UnsupportedException or IndexOutOfRangeException or ArgumentOutOfRangeException
            or KeyNotFoundException or InvalidOperationException or NullReferenceException)
        {
            if (Environment.GetEnvironmentVariable("RVTIO_WHY") == "1") DiagnosticSink.WriteLine($"[meshwhy] cylinder face {face.Marker}: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    private static bool Valid3(double[]? p) => p is { Length: 3 } && double.IsFinite(p[0]) && double.IsFinite(p[1]) && double.IsFinite(p[2]);

    /// <summary>Points of the plane, found by position (per-coordinate tolerance); the lowest id wins when several match.</summary>
    private sealed class PointIndex
    {
        private const double Cell = 1e-6;
        private readonly Dictionary<(long, long), List<int>> grid = new();
        private readonly List<(double X, double Y)> pts = new();
        internal int Count => pts.Count;

        internal int Add(double x, double y)
        {
            pts.Add((x, y));
            var key = ((long)Math.Floor(x / Cell), (long)Math.Floor(y / Cell));
            if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
            l.Add(pts.Count - 1);
            return pts.Count - 1;
        }

        internal int FirstWithin(double x, double y, double tol)
        {
            long cx = (long)Math.Floor(x / Cell), cy = (long)Math.Floor(y / Cell);
            int best = -1;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (grid.TryGetValue((cx + dx, cy + dy), out var l))
                        foreach (int j in l)
                            if ((best < 0 || j < best) && Math.Abs(pts[j].X - x) <= tol && Math.Abs(pts[j].Y - y) <= tol) best = j;
            return best;
        }
    }

    /// <summary>3D position of a loop vertex by its exact parameter point, in order of first insertion.</summary>
    private sealed class LoopPositions
    {
        private readonly Dictionary<(double, double), int> exact = new();
        internal readonly List<(double X, double Y)> Keys = new();
        internal readonly List<double[]> Values = new();
        private readonly PointIndex near = new();

        internal void Set((double X, double Y) key, double[] value)
        {
            if (exact.TryGetValue(key, out int i)) { Values[i] = value; return; }
            exact[key] = Keys.Count;
            Keys.Add(key); Values.Add(value); near.Add(key.X, key.Y);
        }

        internal double[]? Get((double X, double Y) key) => exact.TryGetValue(key, out int i) ? Values[i] : null;

        /// <summary>Position of the first loop vertex within 1e-10 of the point (a vertex snapped to a grid node).</summary>
        internal double[]? Near(double x, double y)
        {
            int i = near.FirstWithin(x, y, Eps);
            return i < 0 ? null : Values[i];
        }
    }

    private static double SignedArea2(IReadOnlyList<(double X, double Y)> uv)
    {
        double ar = 0.0;
        int m = uv.Count;
        for (int i = 0; i < m; i++)
        {
            var p = uv[i]; var q = uv[(i + 1) % m];
            ar += p.X * q.Y - q.X * p.Y;
        }
        return ar;
    }

    /// <summary>All samples of all coedges in loop order (a coedge not oriented to its loop is walked backwards); points closer
    /// than 1e-10 are one vertex (the earlier stays); a closing duplicate gives its 3D position to the first point.</summary>
    private static (List<(double X, double Y)> Uv, LoopPositions Pos3) BuildLoop(List<CylinderSample[]> coedges, IReadOnlyList<MeshCoedge> info)
    {
        var pts = new List<CylinderSample>();
        for (int k = 0; k < coedges.Count; k++)
        {
            var q = info[k].OrientToLoop ? coedges[k] : Enumerable.Reverse(coedges[k]).ToArray();
            foreach (var s in q)
                if (pts.Count == 0 || !CylinderLeaf.Eq(pts[^1].Uv, s.Uv)) pts.Add(s);
        }
        if (pts.Count > 1 && CylinderLeaf.Eq(pts[^1].Uv, pts[0].Uv))
        {
            pts[0] = pts[^1];
            pts.RemoveAt(pts.Count - 1);
        }
        var pos = new LoopPositions();
        foreach (var p in pts) pos.Set(p.Uv, p.P3);
        return (pts.Select(p => p.Uv).ToList(), pos);
    }

    private static Mesh Run(BrFace face, CylinderSurface cyl, double st, double mfl)
    {
        var fr = new CylinderFrame(cyl);
        var env = (X0: cyl.HeightLo / cyl.UScale, Y0: cyl.AngleLo, X1: cyl.HeightHi / cyl.UScale, Y1: cyl.AngleHi);

        // The parameter curves are validated edge by edge in edge-marker order; after the first rejected one the face uses the
        // surface paramOf for all the remaining edges.
        var edges = face.Loops.Select(l => l.Coedges.Select(c => new CylinderEdge(c, st, mfl, fr, env, cyl.AngleLo, cyl.AngleHi)).ToList()).ToList();
        var allow = new bool[face.Loops.Count][];
        for (int li = 0; li < edges.Count; li++) allow[li] = new bool[edges[li].Count];
        bool sticky = true;
        foreach (var (li, k) in Enumerable.Range(0, edges.Count).SelectMany(li => Enumerable.Range(0, edges[li].Count).Select(k => (li, k)))
            .OrderBy(t => face.Loops[t.li].Coedges[t.k].EdgeMarker))
        {
            bool valid = edges[li][k].PcurveValid();
            allow[li][k] = sticky && valid;
            if (!valid) sticky = false;
        }

        var raw = new List<(List<(double X, double Y)> Uv, LoopPositions Pos3)>();
        for (int li = 0; li < edges.Count; li++)
            raw.Add(BuildLoop(edges[li].Select((e, k) => e.Samples(allow[li][k])).ToList(), face.Loops[li].Coedges));
        if (raw.Any(r => r.Uv.Count < 3)) throw new UnsupportedException("degenerate loop");
        var over3d = new LoopPositions();
        foreach (var r in raw)
            for (int i = 0; i < r.Pos3.Keys.Count; i++) over3d.Set(r.Pos3.Keys[i], r.Pos3.Values[i]);

        int outer = 0;
        for (int i = 1; i < raw.Count; i++)
            if (Math.Abs(SignedArea2(raw[i].Uv)) > Math.Abs(SignedArea2(raw[outer].Uv))) outer = i;
        var ou = raw[outer].Uv;
        var box = (X0: ou.Min(p => p.X), Y0: ou.Min(p => p.Y), X1: ou.Max(p => p.X), Y1: ou.Max(p => p.Y));

        var segs = new List<LoopSegment>();
        var stored = new List<List<(double X, double Y)>>();
        foreach (int li in new[] { outer }.Concat(Enumerable.Range(0, raw.Count).Where(i => i != outer)))
        {
            var uv = raw[li].Uv;
            int m = uv.Count;
            // The outer loop must be clockwise in (x, y); the other loops are inverted together with it, so they keep the
            // orientation the B-rep gives them relative to the outer loop (a hole is counter-clockwise, a second patch of the
            // face clockwise).
            var order = Enumerable.Range(0, m).ToList();
            if (SignedArea2(raw[outer].Uv) > 0.0) order = new[] { 0 }.Concat(Enumerable.Range(1, m - 1).AsEnumerable().Reverse()).ToList();
            var uv2 = order.Select(i => uv[i]).ToList();
            var pos3 = uv2.Select(p => over3d.Get(p) ?? fr.S(p.X, p.Y)).ToList();
            var loopSegs = new List<LoopSegment>();
            for (int j = 0; j < m; j++) loopSegs.Add(new LoopSegment(uv2[j], uv2[(j + 1) % m], pos3[j], pos3[(j + 1) % m]));
            loopSegs.Reverse(); // the segments of a loop are inserted in reverse order
            segs.AddRange(loopSegs);
            uv2.Reverse(); // stored loop polygons: positive area for outer loops
            stored.Add(uv2);
        }

        var tree = KdTree.Build(fr.S, box.X0, box.Y0, box.X1, box.Y1, st, mfl);
        List<VEntry> v;
        List<int[]> t;
        if (tree.Nodes.Count == 1) (v, t) = DirectTriangles(stored);
        else (v, t) = CellTriangles(tree, CylinderLeaf.Distribute(tree, segs), stored);

        double[] At(VEntry e) => e.Pos3 ?? over3d.Near(e.X, e.Y) ?? fr.S(e.X, e.Y);
        var pos = v.Select(At).ToList();
        bool reverse = !face.OrientToSurface;
        return Assemble(t.Select(tri => reverse ? new[] { tri[2], tri[1], tri[0] } : tri), pos);
    }

    private static double Area2((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3) =>
        (p2.X - p1.X) * (p3.Y - p1.Y) - (p2.Y - p1.Y) * (p3.X - p1.X);

    /// <summary>The fan triangles (indices into <paramref name="pts"/>) of one contour treated as an outer contour.</summary>
    private static List<int[]> Fan((double X, double Y)[] pts, int[] contour, out bool ok)
    {
        var tris = new List<int[]>();
        foreach (var piece in GeTess.ConvexPieces(pts, new[] { contour }, out ok))
            for (int j = 1; j < piece.Length - 1; j++) tris.Add(new[] { piece[0], piece[j], piece[j + 1] });
        return tris;
    }

    /// <summary>Triangles are clockwise in the parameter plane.</summary>
    private static void Orient(int[] tri, IReadOnlyList<VEntry> v)
    {
        double a = Area2((v[tri[0]].X, v[tri[0]].Y), (v[tri[1]].X, v[tri[1]].Y), (v[tri[2]].X, v[tri[2]].Y)) * 0.5;
        if (a > 0.0) (tri[0], tri[2]) = (tri[2], tri[0]);
    }

    /// <summary>A face covered by one cell: the stored loop polygons (outer loops counter-clockwise, holes clockwise) go through the
    /// profile triangulation as they are; a failed decomposition of a single polygon is accepted.</summary>
    private static (List<VEntry>, List<int[]>) DirectTriangles(List<List<(double X, double Y)>> loops)
    {
        var pts = loops.SelectMany(l => l).ToArray();
        var v = pts.Select(p => new VEntry(p.X, p.Y, false, null)).ToList();
        int k = 0;
        var idx = loops.Select(l => { var r = Enumerable.Range(k, l.Count).ToArray(); k += l.Count; return r; }).ToList();
        var tris = GeTess.TriangulateProfile(pts, idx).Select(t => new[] { t.A, t.B, t.C }).ToList();
        foreach (var tri in tris) Orient(tri, v);
        return (v, tris);
    }

    private static double LoopArea(IReadOnlyList<(double X, double Y)> loop)
    {
        double a = 0.0;
        for (int i = 0; i < loop.Count; i++)
        {
            var p = loop[i]; var q = loop[(i + 1) % loop.Count];
            a += q.Y * p.X - p.Y * q.X;
        }
        return a * 0.5;
    }

    /// <summary>Does the horizontal ray from c to +x cross the edge p1 p2 (half-open rule), and does c lie on the edge.</summary>
    private static (bool Crosses, bool OnBoundary) Crossing((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) c, double tol = 1e-10)
    {
        double ux = p2.X - c.X, uy = p2.Y - c.Y, lx = p1.X - c.X, ly = p1.Y - c.Y;
        if (!(p1.Y < p2.Y)) (lx, ly, ux, uy) = (ux, uy, lx, ly); // l = the lower point, u = the upper one
        if (tol <= Math.Abs(ly))
        {
            if (ly > -tol || tol > uy) return (false, false);
            double dx = lx - ux, dy = ly - uy;
            double v17 = (ux * ly - uy * lx) / Math.Sqrt(dx * dx + dy * dy);
            bool onB = v17 <= tol && -tol <= v17 && ux * lx + uy * ly <= 0.0;
            double t = -uy / dy;
            bool r = false;
            if (0.0 < t && t < 1.0) r = dx * t + ux > 0.0;
            return (r, onB);
        }
        bool zero = Math.Sqrt(lx * lx + ly * ly) <= tol;
        if ((uy > tol || -tol > uy) && (zero || lx > 0.0)) return (true, false);
        return (false, false);
    }

    /// <summary>A leaf without loop edges is a whole cell when the loops enclose its centre (winding count above zero).</summary>
    private static bool CenterInside(List<List<(double X, double Y)>> loops, (double X, double Y) c)
    {
        int count = 0;
        foreach (var lp in loops)
        {
            bool par = false, onb = false;
            for (int i = 0; i < lp.Count; i++)
            {
                var (r, fl) = Crossing(lp[i], lp[(i + 1) % lp.Count], c);
                if (fl) { onb = true; break; }
                if (r) par = !par;
            }
            if (!par && !onb) continue;
            if (LoopArea(lp) < 0.0) { if (!onb) count--; }
            else count++;
        }
        return count > 0;
    }

    private static (List<VEntry>, List<int[]>) CellTriangles(KdTree tree, SortedDictionary<int, CylinderLeaf> leaves,
        List<List<(double X, double Y)>> loops)
    {
        var v = new List<VEntry>();
        var t = new List<int[]>();
        foreach (var lf in leaves.Values)
        {
            var polys = lf.Walk();
            if (polys == null)
            {
                var n = lf.Node;
                if (!CenterInside(loops, ((n.X1 + n.X0) * 0.5, (n.Y1 + n.Y0) * 0.5))) continue;
                int b = v.Count;
                foreach (int j in n.Corners) v.Add(new VEntry(tree.Pts[j].X, tree.Pts[j].Y, false, null));
                t.Add(new[] { b, b + 1, b + 2 });
                t.Add(new[] { b, b + 2, b + 3 });
                continue;
            }
            // the polygons of a leaf are clockwise; a counter-clockwise one would be a hole (triangulateProfile with holes)
            if (polys.Count > 1 && polys.Any(p => LoopArea(p.Select(q => (q.X, q.Y)).ToList()) > 0.0))
            {
                // one outer polygon with holes inside the leaf: triangulateProfile with holes (the outer polygon first)
                var outers = polys.Where(p => LoopArea(p.Select(q => (q.X, q.Y)).ToList()) <= 0.0).ToList();
                if (outers.Count != 1) throw new UnsupportedException("hole polygon inside a leaf");
                var ordered = outers.Concat(polys.Where(p => !outers.Contains(p))).Select(p => Enumerable.Reverse(p).ToList()).ToList();
                int start = v.Count;
                var holePts = new List<(double X, double Y)>();
                var holeLoops = new List<int[]>();
                foreach (var ring in ordered)
                {
                    holeLoops.Add(Enumerable.Range(holePts.Count, ring.Count).ToArray());
                    foreach (var p in ring) { holePts.Add((p.X, p.Y)); v.Add(new VEntry(p.X, p.Y, p.Removable, p.Pos3)); }
                }
                foreach (var tri in GeTess.TriangulateProfile(holePts.ToArray(), holeLoops))
                    t.Add(new[] { start + tri.A, start + tri.B, start + tri.C });
                continue;
            }
            foreach (var poly in polys)
            {
                var rp = Enumerable.Reverse(poly).ToList();
                var pts = rp.Select(p => (p.X, p.Y)).ToArray();
                int b = v.Count;
                foreach (var p in rp) v.Add(new VEntry(p.X, p.Y, p.Removable, p.Pos3));
                foreach (var tri in Fan(pts, Enumerable.Range(0, pts.Length).ToArray(), out _))
                    t.Add(new[] { b + tri[0], b + tri[1], b + tri[2] });
            }
        }
        foreach (var tri in t) Orient(tri, v);
        return (v, RemoveSeams(tree, v, t));
    }

    private static double VecAngle(double dx, double dy)
    {
        double a = Math.Atan2(dy, dx);
        return a < 0.0 ? a + TwoPi : a;
    }

    /// <summary>
    /// Removal of the clip vertices: the triangles around a clip vertex are replaced by the triangulation of the polygon of
    /// their neighbours (clockwise angle order, every neighbour kept), when that succeeds.
    /// </summary>
    internal static List<int[]> RemoveSeams(KdTree tree, List<VEntry> v, List<int[]> t)
    {
        var index = new PointIndex();
        foreach (var p in tree.Pts) index.Add(p.X, p.Y);
        var key = new int[v.Count];
        for (int i = 0; i < v.Count; i++)
        {
            int j = index.FirstWithin(v[i].X, v[i].Y, Eps);
            key[i] = j >= 0 ? j : index.Add(v[i].X, v[i].Y);
        }
        var stars = new Dictionary<int, List<(int Tri, int Corner)>>();
        var alive = Enumerable.Repeat(true, t.Count).ToList();
        for (int ti = 0; ti < t.Count; ti++)
            for (int c = 0; c < 3; c++)
                if (v[t[ti][c]].Removable)
                {
                    if (!stars.TryGetValue(key[t[ti][c]], out var l)) stars[key[t[ti][c]]] = l = new();
                    l.Add((ti, c));
                }
        var xy = v.Select(e => (e.X, e.Y)).ToArray();
        foreach (int kk in stars.Keys.OrderBy(x => x).ToList())
        {
            var ents = stars[kk];
            // the neighbours of the clip vertex by position (a vertex the triangles of both cells list is one neighbour), clockwise by angle; the
            // samples of a straight loop side lie on one ray from the clip vertex and stay all in the ring, the farther one first
            var ringMap = new Dictionary<int, (double Angle, double Dist, int Nb)>();
            VEntry? center = null;
            foreach (var (ti, c) in ents)
            {
                if (!alive[ti]) continue;
                var tri = t[ti];
                center ??= v[tri[c]];
                foreach (int nb in new[] { tri[(c + 1) % 3], tri[(c + 2) % 3] })
                {
                    double dx = v[nb].X - center.Value.X, dy = v[nb].Y - center.Value.Y;
                    ringMap[key[nb]] = (VecAngle(dx, dy), dx * dx + dy * dy, nb);
                }
            }
            if (center == null) continue;
            var ring = ringMap.Values.OrderByDescending(r => r.Angle).ThenByDescending(r => r.Dist).Select(r => r.Nb).ToArray();
            var fan = Fan(xy, ring, out bool ok);
            if (!ok) continue;
            foreach (var (ti, _) in ents) alive[ti] = false;
            foreach (var nt in fan)
            {
                Orient(nt, v);
                t.Add(nt); alive.Add(true);
                for (int c = 0; c < 3; c++)
                {
                    int k2 = key[nt[c]];
                    if (k2 > kk && stars.TryGetValue(k2, out var l)) l.Add((t.Count - 1, c));
                }
            }
        }
        return t.Where((_, i) => alive[i]).ToList();
    }

    /// <summary>Vertices in order of first use; positions closer than 1e-9 on every coordinate are one vertex.</summary>
    private static Mesh Assemble(IEnumerable<int[]> triangles, List<double[]> pos)
    {
        const double cell = 1e-6, tol = 1e-9;
        var grid = new Dictionary<(long, long, long), List<int>>();
        var vertices = new List<double[]>();
        var map = new Dictionary<int, int>();
        int Id(int vi)
        {
            if (map.TryGetValue(vi, out int known)) return known;
            var p = pos[vi];
            long cx = (long)Math.Floor(p[0] / cell), cy = (long)Math.Floor(p[1] / cell), cz = (long)Math.Floor(p[2] / cell);
            int best = -1;
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    for (long dz = -1; dz <= 1; dz++)
                        if (grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var l))
                            foreach (int j in l)
                                if ((best < 0 || j < best) && Math.Abs(vertices[j][0] - p[0]) <= tol && Math.Abs(vertices[j][1] - p[1]) <= tol
                                    && Math.Abs(vertices[j][2] - p[2]) <= tol) best = j;
            if (best < 0)
            {
                best = vertices.Count;
                vertices.Add(p);
                if (!grid.TryGetValue((cx, cy, cz), out var own)) grid[(cx, cy, cz)] = own = new();
                own.Add(best);
            }
            return map[vi] = best;
        }
        var tris = triangles.Select(tri => new[] { Id(tri[0]), Id(tri[1]), Id(tri[2]) }).ToArray();
        return new Mesh { Vertices = vertices.ToArray(), Triangles = tris };
    }
}
