namespace RvtIO.Geometry.Meshing;

/// <summary>
/// The emit stage of the mesher for NURBS surface faces: grid points that are only border crossings of a loop segment (cell
/// border vertices created by the clipping) are removed from the mesh again; the triangles around such a point are replaced by
/// the triangulation of the polygon of its neighbours, ordered by clockwise angle.
/// </summary>
internal static class FreeformSeams
{
    private const double TolUv = 1e-10;

    /// <summary>
    /// Pole bits as the mesher sees them: 4 u-min, 1 u-max, 8 v-min, 2 v-max; a max side pole is only reported together with
    /// the min side pole.
    /// </summary>
    private static int PoleMask(Func<double, double, Pt3> s, double[] env)
    {
        double u0 = env[0], u1 = env[1], v0 = env[2], v1 = env[3];
        var us = Enumerable.Range(0, 6).Select(k => u0 + (u1 - u0) * k / 5.0).ToArray();
        var vs = Enumerable.Range(0, 6).Select(k => v0 + (v1 - v0) * k / 5.0).ToArray();
        var p00 = s(u0, v0); var p11 = s(u1, v1);
        bool Same(Pt3 a, Pt3 b) => (a - b).Norm <= 1e-10;
        bool uMin = vs.All(v => Same(p00, s(u0, v))), uMax = vs.All(v => Same(p11, s(u1, v)));
        bool vMin = us.All(u => Same(p00, s(u, v0))), vMax = us.All(u => Same(p11, s(u, v1)));
        int mask = 0;
        if (uMin) { mask |= 4; if (uMax) mask |= 1; }
        if (vMin) { mask |= 8; if (vMax) mask |= 2; }
        return mask;
    }

    private static (int Kind, int Val) Rank(FfVtx v) =>
        v.Pid >= 0 ? (0, v.Pid) : v.PoolHint >= 0 ? (0, v.PoolHint) : (1, Math.Max(v.Seq, 0));

    /// <summary>ccwOut: the triangles must be counter-clockwise in uv.</summary>
    internal static List<(FfVtx, FfVtx, FfVtx)> RemoveBoundaryVertices(List<(FfVtx A, FfVtx B, FfVtx C)> tris,
        Func<double, double, Pt3> s, double[] env, bool ccwOut)
    {
        var ids = new Dictionary<(long, long), int>();
        var vtx = new List<FfVtx>();
        int Vid(FfVtx v)
        {
            var k = FreeformGrid.Key(v.U, v.V);
            if (!ids.TryGetValue(k, out int i)) { i = vtx.Count; ids[k] = i; vtx.Add(v); }
            return i;
        }
        var t = tris.Select(x => new[] { Vid(x.A), Vid(x.B), Vid(x.C) }).ToList();
        var alive = Enumerable.Repeat(true, t.Count).ToList();
        // a merged vertex is removable when every record merged into it is a border crossing
        var b17 = Enumerable.Repeat(true, vtx.Count).ToArray();
        var rank = new (int Kind, int Val)?[vtx.Count];
        foreach (var (a, b, c) in tris)
            foreach (var v in new[] { a, b, c })
            {
                int m = ids[FreeformGrid.Key(v.U, v.V)];
                if (v.F17 == 0) b17[m] = false;
                var r = Rank(v);
                if (rank[m] == null || r.CompareTo(rank[m]!.Value) < 0) rank[m] = r;
            }
        int mask = PoleMask(s, env);
        double u0 = env[0], u1 = env[1], v0 = env[2], v1 = env[3];
        bool AtPole(int m)
        {
            var v = vtx[m];
            return ((mask & 4) != 0 && Math.Abs(v.U - u0) <= 1e-9) || ((mask & 1) != 0 && Math.Abs(v.U - u1) <= 1e-9)
                || ((mask & 8) != 0 && Math.Abs(v.V - v0) <= 1e-9) || ((mask & 2) != 0 && Math.Abs(v.V - v1) <= 1e-9);
        }
        double Area(int a, int b, int c)
        {
            var A = vtx[a]; var B = vtx[b]; var C = vtx[c];
            return ((A.V - B.V) * (C.U - A.U) + (C.V - A.V) * (B.U - A.U)) * 0.5;
        }
        int[] Fix(int a, int b, int c) => (Area(a, b, c) > 0.0) == ccwOut ? new[] { a, b, c } : new[] { a, c, b };
        int Cmp(int x, int y) { int c = rank[x]!.Value.CompareTo(rank[y]!.Value); return c != 0 ? c : x.CompareTo(y); }

        var star = new Dictionary<int, List<int>>();
        for (int k = 0; k < t.Count; k++)
            for (int j = 0; j < 3; j++)
            {
                int m = t[k][j];
                if (b17[m] && !AtPole(m))
                {
                    if (!star.TryGetValue(m, out var l)) star[m] = l = new List<int>();
                    l.Add(k);
                }
            }
        var keys = star.Keys.ToList();
        keys.Sort(Cmp);
        foreach (int m in keys)
        {
            var ks = star[m].Where(k => alive[k]).ToList();
            if (ks.Count == 0) continue;
            var center = vtx[m];
            var ring = new Dictionary<double, int>();
            foreach (int k in ks)
                foreach (int w in t[k])
                {
                    if (w == m) continue;
                    var q = vtx[w];
                    double ang = Math.Atan2(q.V - center.V, q.U - center.U);
                    if (ang < 0) ang += 2.0 * Math.PI;
                    ring[2.0 * Math.PI - ang] = w;
                }
            var order = ring.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
            var pts = order.Select(w => (vtx[w].U, vtx[w].V)).ToArray();
            var pieces = GeTess.ConvexPieces(pts, new[] { Enumerable.Range(0, order.Length).ToArray() });
            var fans = new List<int[]>();
            foreach (var piece in pieces)
                for (int j = 1; j < piece.Length - 1; j++) fans.Add(new[] { order[piece[0]], order[piece[j]], order[piece[j + 1]] });
            if (fans.Count == 0) continue;
            foreach (int k in ks) alive[k] = false;
            foreach (var f in fans)
            {
                var nt = Fix(f[0], f[1], f[2]);
                t.Add(nt); alive.Add(true);
                foreach (int w in nt)
                    if (Cmp(w, m) > 0 && star.TryGetValue(w, out var l)) l.Add(t.Count - 1);
            }
        }
        var res = new List<(FfVtx, FfVtx, FfVtx)>();
        for (int k = 0; k < t.Count; k++)
            if (alive[k]) res.Add((vtx[t[k][0]], vtx[t[k][1]], vtx[t[k][2]]));
        return res;
    }
}
