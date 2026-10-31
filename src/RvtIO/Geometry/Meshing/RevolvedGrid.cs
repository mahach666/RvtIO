namespace RvtIO.Geometry.Meshing;

/// <summary>Evaluation of the mesher surface of a revolved face (u = profile parameter, v = rotation).</summary>
internal sealed class RevolvedEval
{
    private readonly RevolvedSurface s;
    public RevolvedEval(RevolvedSurface surface) => s = surface;

    public double[] Pt(double u, double v)
    {
        double[] p;
        if (s.ProfileAt != null) p = s.ProfileAt(u);
        else if (s.ProfileStart is { } p0)
        {
            var d = s.ProfileDir!;
            double t = u - s.U0;
            p = new[] { p0[0] + t * d[0], p0[1] + t * d[1], p0[2] + t * d[2] };
        }
        else
        {
            double t = s.ArcEps * (u - s.U0), c = Math.Cos(t), sn = Math.Sin(t);
            var e1 = s.ArcE1!; var e2 = s.ArcE2!; var cen = s.ArcCenter!;
            p = new[]
            {
                cen[0] + s.ArcRadius * (c * e1[0] + sn * e2[0]),
                cen[1] + s.ArcRadius * (c * e1[1] + sn * e2[1]),
                cen[2] + s.ArcRadius * (c * e1[2] + sn * e2[2]),
            };
        }
        var o = s.Origin; var k = s.Axis;
        double qx = p[0] - o[0], qy = p[1] - o[1], qz = p[2] - o[2];
        double ang = v - s.V0, cs = Math.Cos(ang), sa = Math.Sin(ang);
        double cx = k[1] * qz - k[2] * qy, cy = k[2] * qx - k[0] * qz, cz = k[0] * qy - k[1] * qx;
        double kd = k[0] * qx + k[1] * qy + k[2] * qz, om = 1.0 - cs;
        return new[]
        {
            o[0] + qx * cs + cx * sa + k[0] * kd * om,
            o[1] + qy * cs + cy * sa + k[1] * kd * om,
            o[2] + qz * cs + cz * sa + k[2] * kd * om,
        };
    }
}

/// <summary>
/// The adaptive bisection grid of the mesher's face tessellator over the uv envelope: LIFO stack of cells, maximum level 12, a
/// cell is split at its midpoint in the direction whose chord deviation (3 samples per direction) exceeds the surface
/// tolerance, then for warped diagonals, then when a cell edge is longer than the maximum edge length.
/// </summary>
internal static class RevolvedGrid
{
    private const int MaxLevel = 12;
    private const double Eps = 1e-10;
    // chord sample weights (bit patterns of the mesher constants)
    private static readonly double[] WA =
    {
        BitConverter.Int64BitsToDouble(0x3FD3DB5C5A901A2D), 0.5, BitConverter.Int64BitsToDouble(0x3FE61251D2B7F2E9),
    };
    private static readonly double[] WB = { WA[2], WA[1], WA[0] };

    internal sealed class Cell
    {
        public int I, Lvl, Dir = -1, Ch0 = -1, Ch1 = -1;
        public double U0, U1, V0, V1, Val;
        /// <summary>Grid point ids of the corners (u0,v0) (u0,v1) (u1,v1) (u1,v0).</summary>
        public int[] Cn = null!;
        public List<(double U, double V)> T = new();
        public List<((double U, double V) A, (double U, double V) B)> Recs = new();
    }

    private static double Sq(double[] p, double[] q)
    {
        double a = p[0] - q[0], b = p[1] - q[1], c = p[2] - q[2];
        return a * a + b * b + c * c;
    }

    private static double[] Lin(double[] p, double a, double[] q, double b) =>
        new[] { p[0] * a + q[0] * b, p[1] * a + q[1] * b, p[2] * a + q[2] * b };

    private static double[] Mid(double[] p, double[] q) => new[] { (p[0] + q[0]) * 0.5, (p[1] + q[1]) * 0.5, (p[2] + q[2]) * 0.5 };

    private static double[] ClosestOnSegment(double[] a, double[] b, double[] p)
    {
        double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
        double dd = dx * dx + dy * dy + dz * dz;
        if (dd == 0.0) return a;
        double t = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy + (p[2] - a[2]) * dz) / dd;
        t = Math.Min(1.0, Math.Max(0.0, t));
        return new[] { a[0] + t * dx, a[1] + t * dy, a[2] + t * dz };
    }

    /// <summary>Cell test: -1 leaf, 0 split u, 1 split v.</summary>
    private static int CellTest(RevolvedEval surf, Cell c, double tol, double maxLen)
    {
        double u0 = c.U0, u1 = c.U1, v0 = c.V0, v1 = c.V1;
        double tol2 = tol * tol;
        double um = (u0 + u1) * 0.5, vm = (v0 + v1) * 0.5;
        var pb = surf.Pt(um, v0); var pl = surf.Pt(u0, vm); var pr = surf.Pt(u1, vm); var pt = surf.Pt(um, v1);
        double devu = 0.0, devv = 0.0;
        for (int k = 0; k < 3; k++)
        {
            double a = WA[k], b = WB[k];
            var su = surf.Pt(u0 * a + b * u1, vm);
            var chordU = Lin(pl, a, pr, b);
            var sv = surf.Pt(um, v0 * a + b * v1);
            var chordV = Lin(pb, a, pt, b);
            double du = Sq(chordU, su), dv = Sq(chordV, sv);
            if (k > 0) { du = Math.Max(du, devu); dv = Math.Max(dv, devv); }
            devu = du; devv = dv;
        }
        if (tol2 < devu || tol2 < devv) return devu <= devv ? 1 : 0;
        var c00 = surf.Pt(u0, v0); var c01 = surf.Pt(u0, v1); var c10 = surf.Pt(u1, v0); var c11 = surf.Pt(u1, v1);
        var m1 = Mid(c00, c11); var m2 = Mid(c01, c10);
        double d1 = Math.Min(Sq(m1, ClosestOnSegment(pb, pt, m1)), Sq(m1, ClosestOnSegment(pl, pr, m1)));
        double d2 = Math.Min(Sq(m2, ClosestOnSegment(pb, pt, m2)), Sq(m2, ClosestOnSegment(pl, pr, m2)));
        if (tol2 * 100.0 < d1 || tol2 * 100.0 < d2) return d1 <= d2 ? 1 : 0;
        double ml2 = maxLen * maxLen;
        if (Sq(c11, c01) > ml2) return 0;
        if (Sq(c10, c00) > ml2) return 0;
        if (Sq(c11, c10) > ml2) return 1;
        if (Sq(c01, c00) > ml2) return 1;
        return -1; // the normal angle criterion is disabled (angle tolerance 360 degrees)
    }

    /// <summary>All cells in creation order (index = creation order) and the grid points in creation order.</summary>
    internal static (List<Cell> Cells, List<(double U, double V)> Points) Build(RevolvedEval surf, double[] env, double tol, double maxLen)
    {
        var cells = new List<Cell>
        {
            new() { I = 0, U0 = env[0], U1 = env[1], V0 = env[2], V1 = env[3], Lvl = 0, Cn = new[] { 0, 1, 2, 3 } },
        };
        var gp = new List<(double U, double V)> { (env[0], env[2]), (env[0], env[3]), (env[1], env[3]), (env[1], env[2]) };

        int Split(int ci, int d, double val)
        {
            var c = cells[ci];
            double lo = d == 0 ? c.U0 : c.V0, hi = d == 0 ? c.U1 : c.V1;
            if (Math.Abs(val - lo) <= Eps || Math.Abs(val - hi) <= Eps) return -1;
            int n = cells.Count;
            c.Ch0 = n; c.Ch1 = n + 1; c.Dir = d; c.Val = val;
            if (d == 0) { gp.Add((val, c.V1)); gp.Add((val, c.V0)); }
            else { gp.Add((c.U1, val)); gp.Add((c.U0, val)); }
            int p = gp.Count - 2;
            var cn = c.Cn;
            if (d == 0)
            {
                cells.Add(new Cell { I = n, U0 = c.U0, U1 = val, V0 = c.V0, V1 = c.V1, Lvl = c.Lvl + 1, Cn = new[] { cn[0], cn[1], p, p + 1 } });
                cells.Add(new Cell { I = n + 1, U0 = val, U1 = c.U1, V0 = c.V0, V1 = c.V1, Lvl = c.Lvl + 1, Cn = new[] { p + 1, p, cn[2], cn[3] } });
            }
            else
            {
                cells.Add(new Cell { I = n, U0 = c.U0, U1 = c.U1, V0 = c.V0, V1 = val, Lvl = c.Lvl + 1, Cn = new[] { cn[0], p + 1, p, cn[3] } });
                cells.Add(new Cell { I = n + 1, U0 = c.U0, U1 = c.U1, V0 = val, V1 = c.V1, Lvl = c.Lvl + 1, Cn = new[] { p + 1, cn[1], cn[2], p } });
            }
            return n;
        }

        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int ci = stack.Pop();
            var c = cells[ci];
            if (c.Lvl > MaxLevel) continue;
            int r = CellTest(surf, c, tol, maxLen);
            if (r < 0) continue;
            double lo = r == 0 ? c.U0 : c.V0, hi = r == 0 ? c.U1 : c.V1;
            int n = Split(ci, r, (lo + hi) * 0.5);
            if (n >= 0) { stack.Push(n); stack.Push(n + 1); }
        }
        return (cells, gp);
    }
}
