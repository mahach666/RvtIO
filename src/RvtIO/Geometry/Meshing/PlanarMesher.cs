namespace RvtIO.Geometry.Meshing;

internal static class PlanarMesher
{
    private const double Eps = PlanarGrid.Eps;
    private const double VertexTolerance = 0.0005233832795;
    private const double CurvedWeldTolerance = 1e-6;
    private const double TwoPi = 2.0 * Math.PI;

    private sealed class Seg
    {
        public (double X, double Y) A, B;
        public double[] A3 = null!, B3 = null!;
        public int E;
        public double[][]? Arc;
    }

    /// <summary>Per face state: the 3D point of every uv loop vertex (the B-rep vertices are not exactly on the plane).</summary>
    private sealed class Context
    {
        public readonly Dictionary<(double, double), double[]> Xyz = new();
        public double[] O = null!, U = null!, V = null!;
    }

    private sealed class Pool
    {
        public readonly List<(double X, double Y)> Xy = new();
        public readonly List<int> Cut = new();
        public int[]? Uid;
        public List<double[]>? Pos;
        public int Add(double x, double y, int cut) { Xy.Add((x, y)); Cut.Add(cut); return Xy.Count - 1; }
    }

    /// <summary>Lmax from the extents diagonal and the level of detail is the caller's job (Tolerances.MaxEdgeLength).</summary>
    internal static bool TryMesh(BrFace face, double lmax, out Mesh mesh)
    {
        mesh = null!;
        if (face.Plane is not { } plane || !(lmax > 0.0) || double.IsInfinity(lmax) || face.Loops.Count == 0) return false;
        if (!Valid3(plane.Origin) || !Valid3(plane.U) || !Valid3(plane.V)) return false;
        foreach (var loop in face.Loops)
        {
            if (loop.Coedges.Count == 0) return false;
            foreach (var c in loop.Coedges)
            {
                if (!Valid3(c.Start) || !Valid3(c.End)) return false;
                if (c.Samples is { } s && (s.Length < 2 || s.Any(p => !Valid3(p)))) return false;
                if (c.UvStart is { } us && (us.Length != 2 || !double.IsFinite(us[0]) || !double.IsFinite(us[1]))) return false;
                if (c.UvEnd is { } ue && (ue.Length != 2 || !double.IsFinite(ue[0]) || !double.IsFinite(ue[1]))) return false;
                if ((c.UvStart == null) != (c.UvEnd == null)) return false;
            }
        }
        try
        {
            var ctx = new Context
            {
                O = plane.Origin, U = plane.U, V = plane.V,
            };
            var loops = UvLoops(face, lmax, ctx);
            if (loops.Any(l => l.Count == 0)) return false;
            bool tessellated = Tessellate(face.Flipped, lmax, loops, ctx, out mesh);
            if (!tessellated && Environment.GetEnvironmentVariable("RVTIO_WHY") == "1")
                DiagnosticSink.WriteLine($"[meshwhy] plane face {face.Marker}: tessellation refused, loops {loops.Count} points {string.Join("/", loops.Select(l => l.Count))} samples {string.Join("/", face.Loops.SelectMany(l => l.Coedges).Select(c => c.Samples?.Length ?? 0))} pts {string.Join(" ", loops[0].Select(q => $"({q.X:F5},{q.Y:F5})"))}");
            return tessellated;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or KeyNotFoundException
            or InvalidOperationException or NullReferenceException)
        {
            if (Environment.GetEnvironmentVariable("RVTIO_WHY") == "1") DiagnosticSink.WriteLine($"[meshwhy] plane face {face.Marker}: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    private static bool Valid3(double[]? p) => p is { Length: 3 } && double.IsFinite(p[0]) && double.IsFinite(p[1]) && double.IsFinite(p[2]);

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static (double X, double Y) Proj(double[] q, Context ctx)
    {
        double[] d = { q[0] - ctx.O[0], q[1] - ctx.O[1], q[2] - ctx.O[2] };
        return (Dot(d, ctx.U), Dot(d, ctx.V));
    }

    private static double Dist(double[] a, double[] b)
    {
        double x = a[0] - b[0], y = a[1] - b[1], z = a[2] - b[2];
        return Math.Sqrt(x * x + y * y + z * z);
    }

    /// <summary>Smallest power of two n with length / n &lt;= lmax.</summary>
    internal static int Pow2Count(double length, double lmax)
    {
        int n = 1;
        while (length / n > lmax && n < (1 << 20)) n *= 2;
        return n;
    }

    /// <summary>
    /// uv loops as the mesher hands them to the triangulation. The uv of an edge point is the stored uv of the edge point on
    /// this face (the projection of the 3D point when none is stored).
    /// </summary>
    private static List<List<(double X, double Y)>> UvLoops(BrFace face, double lmax, Context ctx)
    {
        var loops = new List<List<(double X, double Y)>>();
        foreach (var loop in face.Loops)
        {
            int n = loop.Coedges.Count;
            var seg = new List<Seg>(n);
            foreach (var c in loop.Coedges)
            {
                var a3 = c.Start!; var b3 = c.End!;
                (double X, double Y) a, b;
                if (c.UvStart is { } us && c.UvEnd is { } ue)
                {
                    a = (us[0], us[1]); b = (ue[0], ue[1]);
                    var pa = Proj(a3, ctx); var pb = Proj(b3, ctx);
                    double keep = Math.Abs(a.X - pa.X) + Math.Abs(a.Y - pa.Y) + Math.Abs(b.X - pb.X) + Math.Abs(b.Y - pb.Y);
                    double swap = Math.Abs(b.X - pa.X) + Math.Abs(b.Y - pa.Y) + Math.Abs(a.X - pb.X) + Math.Abs(a.Y - pb.Y);
                    if (swap < keep) (a, b) = (b, a);
                }
                else { a = Proj(a3, ctx); b = Proj(b3, ctx); }
                seg.Add(new Seg { A = a, B = b, A3 = a3, B3 = b3, E = c.EdgeMarker, Arc = c.Samples });
            }
            for (int i = 0; i < n; i++)
            {
                var s0 = seg[i]; var s1 = seg[(i + 1) % n];
                double gap = Dist(s0.B3, s1.A3);
                if (0.0 < gap && gap <= (s0.Arc != null || s1.Arc != null ? CurvedWeldTolerance : VertexTolerance))
                {
                    if (s0.E >= 0 && s1.E >= 0 && s1.E < s0.E) { s0.B = s1.A; s0.B3 = s1.A3; }
                    else { s1.A = s0.B; s1.A3 = s0.B3; }
                }
            }
            if (face.Flipped)
            {
                var rev = new List<Seg>(n);
                for (int i = n - 1; i >= 0; i--)
                {
                    var s = seg[i];
                    rev.Add(new Seg { A = s.B, B = s.A, A3 = s.B3, B3 = s.A3, E = s.E, Arc = s.Arc is { } arc ? Enumerable.Reverse(arc).ToArray() : null });
                }
                seg = rev;
            }
            var pts = new List<(double X, double Y)>();
            foreach (var s in seg)
            {
                List<(double X, double Y)> sp;
                if (s.Arc is { } arc)
                {
                    sp = new List<(double X, double Y)>();
                    for (int i = 0; i < arc.Length - 1; i++)
                    {
                        var q2 = Proj(arc[i], ctx);
                        sp.Add(q2);
                        ctx.Xyz.TryAdd(q2, (double[])arc[i].Clone());
                    }
                    sp[0] = s.A;
                }
                else
                {
                    int k = Pow2Count(Dist(s.B3, s.A3), lmax);
                    sp = new List<(double X, double Y)> { s.A };
                    for (int j = 1; j < k; j++)
                        sp.Add((s.A.X + (s.B.X - s.A.X) * ((double)j / k), s.A.Y + (s.B.Y - s.A.Y) * ((double)j / k)));
                    for (int j = 1; j < k; j++)
                    {
                        double f = (double)j / k;
                        ctx.Xyz.TryAdd(sp[j], new[] { s.A3[0] + (s.B3[0] - s.A3[0]) * f, s.A3[1] + (s.B3[1] - s.A3[1]) * f, s.A3[2] + (s.B3[2] - s.A3[2]) * f });
                    }
                }
                ctx.Xyz.TryAdd(sp[0], s.A3);
                ctx.Xyz.TryAdd(s.B, s.B3);
                pts.AddRange(sp);
                pts.Add(s.B);
            }
            var ded = new List<(double X, double Y)>();
            foreach (var p in pts)
                if (ded.Count == 0 || !PlanarGrid.Close(p, ded[^1])) ded.Add(p); // coinciding consecutive points: the first stays
            while (ded.Count > 1 && PlanarGrid.Close(ded[0], ded[^1])) ded.RemoveAt(ded.Count - 1);
            loops.Add(ded);
        }
        return loops;
    }

    private static double[] PoolXyz(Pool pool, int i, Context ctx)
    {
        var p = pool.Xy[i];
        if (ctx.Xyz.TryGetValue(p, out var q)) return q;
        return new[]
        {
            ctx.O[0] + ctx.U[0] * p.X + ctx.V[0] * p.Y,
            ctx.O[1] + ctx.U[1] * p.X + ctx.V[1] * p.Y,
            ctx.O[2] + ctx.U[2] * p.X + ctx.V[2] * p.Y,
        };
    }

    private static bool Tessellate(bool flipped, double lmax, List<List<(double X, double Y)>> uvl, Context ctx, out Mesh mesh)
    {
        double su = Math.Sqrt(Dot(ctx.U, ctx.U)), sv = Math.Sqrt(Dot(ctx.V, ctx.V));
        var all = uvl.SelectMany(l => l).ToList();
        var env = (all.Min(p => p.X), all.Min(p => p.Y), all.Max(p => p.X), all.Max(p => p.Y));
        var (leaves, gp) = PlanarGrid.BspCells(env, lmax, su, sv);
        var pool = new Pool();
        var tris = new List<int[]>(); // [live, a, b, c] pool indices
        if (leaves.Count == 1)
        {
            var idx = new List<int[]>();
            int k = 0;
            foreach (var lp in uvl)
            {
                idx.Add(Enumerable.Range(k, lp.Count).ToArray());
                k += lp.Count;
                foreach (var p in lp) pool.Add(p.X, p.Y, 0);
            }
            foreach (var t in GeTess.TriangulateProfile(pool.Xy, idx)) tris.Add(new[] { 1, t.A, t.B, t.C });
            if (tris.Count == 0 && Environment.GetEnvironmentVariable("RVTIO_WHY") == "1") DiagnosticSink.WriteLine($"[meshwhy] single cell: the profile triangulation of {pool.Xy.Count} points gave no triangle; loop areas {string.Join("/", uvl.Select(l => { double a = 0; for (int i = 0; i < l.Count; i++) { var p = l[i]; var q = l[(i + 1) % l.Count]; a += p.X * q.Y - q.X * p.Y; } return (a / 2).ToString("G4"); }))} flipped {flipped}");
            return Finish(pool, tris, ctx, flipped, false, out mesh);
        }
        var loopsW = PlanarGrid.LoopArrays(uvl);
        foreach (var g in gp) pool.Add(g.X, g.Y, 0);
        foreach (var cell in leaves)
        {
            var cps = PlanarGrid.CellPolygons(cell, loopsW, uvl);
            // a cell no loop edge touches that lies inside the face: its four corners, starting at (u0, v1)
            var polys = cps ?? new List<List<(double X, double Y, int Cut)>>
            {
                new() { (cell.U0, cell.V1, 0), (cell.U1, cell.V1, 0), (cell.U1, cell.V0, 0), (cell.U0, cell.V0, 0) },
            };
            var cornerPts = new[]
            {
                ((cell.U0, cell.V0), cell.Corners[0]), ((cell.U0, cell.V1), cell.Corners[1]),
                ((cell.U1, cell.V1), cell.Corners[2]), ((cell.U1, cell.V0), cell.Corners[3]),
            };
            var idxs = new List<int[]>();
            foreach (var poly in polys)
            {
                var ids = new List<int>();
                for (int i = poly.Count - 1; i >= 0; i--) // every loop is reversed before the triangulation
                {
                    var (x, y, cut) = poly[i];
                    int gid = -1;
                    foreach (var (cp, g) in cornerPts)
                        if (PlanarGrid.Close((x, y), cp)) { gid = g; break; }
                    if (gid < 0) gid = pool.Add(x, y, cut);
                    else { pool.Xy[gid] = (x, y); pool.Cut[gid] = cut; } // a polygon vertex on a cell corner overwrites the shared grid entry
                    ids.Add(gid);
                }
                idxs.Add(ids.ToArray());
            }
            if (idxs.Count == 0) continue;
            foreach (var t in GeTess.TriangulateProfile(pool.Xy, idxs)) tris.Add(new[] { 1, t.A, t.B, t.C });
        }
        bool removed = RemoveSeams(pool, tris, ctx);
        return Finish(pool, tris, ctx, flipped, removed, out mesh);
    }

    private static double Angle2d(double x, double y)
    {
        double a = Math.Atan2(y, x);
        return a < 0 ? a + TwoPi : a;
    }

    private static double TriArea(Pool pool, int a, int b, int c)
    {
        var p = pool.Xy[a]; var q = pool.Xy[b]; var r = pool.Xy[c];
        return ((q.X - p.X) * (r.Y - p.Y) - (r.X - p.X) * (q.Y - p.Y)) * 0.5;
    }

    /// <summary>Vertex cache on the 3D points: a pool entry referenced by a triangle gets the id of the first entry at the same 3D
    /// position (Eps); flag[id] stays set only while every entry at that position is a clip-created (removable) vertex.</summary>
    private static (int[] Uid, List<int> Flag, List<double[]> Pos) UniqueIds(Pool pool, List<int[]> tris, Context ctx)
    {
        var used = new int[pool.Xy.Count];
        foreach (var t in tris) for (int j = 1; j < 4; j++) used[t[j]]++;
        var uid = new int[pool.Xy.Count];
        Array.Fill(uid, -1);
        var flag = new List<int>();
        var cache = new Dictionary<(long, long, long), List<int>>();
        var pos = new List<double[]>();
        for (int i = 0; i < pool.Xy.Count; i++)
        {
            if (used[i] == 0) continue;
            var p = PoolXyz(pool, i, ctx);
            var key = (CellOf(p[0]), CellOf(p[1]), CellOf(p[2]));
            int found = -1;
            for (int dx = -1; dx <= 1 && found < 0; dx++)
                for (int dy = -1; dy <= 1 && found < 0; dy++)
                    for (int dz = -1; dz <= 1 && found < 0; dz++)
                        if (cache.TryGetValue((key.Item1 + dx, key.Item2 + dy, key.Item3 + dz), out var list))
                            foreach (int j in list)
                                if (Math.Max(Math.Abs(pos[j][0] - p[0]), Math.Max(Math.Abs(pos[j][1] - p[1]), Math.Abs(pos[j][2] - p[2]))) <= Eps)
                                {
                                    found = j;
                                    break;
                                }
            if (found < 0)
            {
                found = pos.Count;
                pos.Add(p); flag.Add(1);
                if (!cache.TryGetValue(key, out var l)) cache[key] = l = new List<int>();
                l.Add(found);
            }
            uid[i] = found;
            if (flag[found] != 0) flag[found] = pool.Cut[i];
        }
        return (uid, flag, pos);
    }

    private static long CellOf(double c) => (long)Math.Floor(c / 1e-7);

    /// <summary>Remove the vertices created by clipping; true when a triangle was replaced.</summary>
    private static bool RemoveSeams(Pool pool, List<int[]> tris, Context ctx)
    {
        var (uid, flag, pos) = UniqueIds(pool, tris, ctx);
        foreach (var t in tris) // all triangles clockwise in uv
            if (TriArea(pool, t[1], t[2], t[3]) > 0) (t[1], t[3]) = (t[3], t[1]);
        var inc = new Dictionary<int, List<(int K, int J)>>(); // vertex id -> (triangle, corner)
        for (int k = 0; k < tris.Count; k++)
            for (int j = 0; j < 3; j++)
            {
                int pv = tris[k][1 + j];
                if (pool.Cut[pv] != 0 && flag[uid[pv]] != 0)
                {
                    if (!inc.TryGetValue(uid[pv], out var l)) inc[uid[pv]] = l = new List<(int, int)>();
                    l.Add((k, j));
                }
            }
        foreach (int key in inc.Keys.OrderBy(x => x))
        {
            var lst = inc[key];
            (double X, double Y)? p0 = null;
            var ring = new Dictionary<double, int>();
            foreach (var (k, j) in lst)
            {
                var t = tris[k];
                if (t[0] == 0) continue;
                p0 ??= pool.Xy[t[1 + j]];
                foreach (int jj in new[] { (j + 1) % 3, (j + 2) % 3 })
                {
                    int nb = t[1 + jj];
                    ring[TwoPi - Angle2d(pool.Xy[nb].X - p0.Value.X, pool.Xy[nb].Y - p0.Value.Y)] = nb;
                }
            }
            if (ring.Count < 3) continue;
            var loop = ring.Keys.OrderBy(a => a).Select(a => ring[a]).ToArray();
            var created = GeTess.TriangulateProfile(pool.Xy, new[] { loop }, new[] { true });
            if (created.Count == 0) continue;
            foreach (var (k, _) in lst) tris[k][0] = 0;
            int baseIndex = tris.Count;
            for (int i = 0; i < created.Count; i++)
            {
                var nt = created[i];
                var tri = new[] { 1, nt.A, nt.B, nt.C };
                if (TriArea(pool, nt.A, nt.B, nt.C) > 0) (tri[1], tri[3]) = (tri[3], tri[1]);
                tris.Add(tri);
                for (int kk = 0; kk < 3; kk++)
                {
                    int u2 = uid[tri[1 + kk]];
                    if (u2 > key && inc.TryGetValue(u2, out var l)) l.Add((baseIndex + i, kk));
                }
            }
        }
        pool.Uid = uid;
        pool.Pos = pos;
        return tris.Any(t => t[0] == 0);
    }

    /// <summary>Live triangles in list order (corners 0 and 2 swapped unless the face is flipped, degenerate ones dropped) and the
    /// vertex list: ascending vertex-cache id, or first appearance in the triangle list after seam removal.</summary>
    private static bool Finish(Pool pool, List<int[]> tris, Context ctx, bool flipped, bool appearanceOrder, out Mesh mesh)
    {
        if (pool.Uid == null)
        {
            var (uid, _, pos) = UniqueIds(pool, tris, ctx);
            pool.Uid = uid; pool.Pos = pos;
        }
        var live = new List<int[]>();
        foreach (var t in tris)
        {
            if (t[0] == 0) continue;
            int[] ids = { pool.Uid[t[1]], pool.Uid[t[2]], pool.Uid[t[3]] };
            if (!flipped) (ids[0], ids[2]) = (ids[2], ids[0]);
            if (ids[0] == ids[1] || ids[1] == ids[2] || ids[0] == ids[2]) continue;
            live.Add(ids);
        }
        List<int> used;
        if (!appearanceOrder) used = live.SelectMany(i => i).Distinct().OrderBy(i => i).ToList();
        else
        {
            used = new List<int>();
            var seen = new HashSet<int>();
            foreach (var ids in live) foreach (int i in ids) if (seen.Add(i)) used.Add(i);
        }
        var vmap = new Dictionary<int, int>();
        for (int k = 0; k < used.Count; k++) vmap[used[k]] = k;
        mesh = new Mesh
        {
            Vertices = used.Select(i => (double[])pool.Pos![i].Clone()).ToArray(),
            Triangles = live.Select(ids => new[] { vmap[ids[0]], vmap[ids[1]], vmap[ids[2]] }).ToArray(),
        };
        return mesh.Triangles.Length > 0;
    }
}
