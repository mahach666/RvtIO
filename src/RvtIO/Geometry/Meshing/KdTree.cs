namespace RvtIO.Geometry.Meshing;

internal sealed class KdTree
{
    internal const double Eps = 1e-10;
    private const int MaxDepth = 12;
    private static readonly double[] W1 = { 0.3102637180713, 0.5, 0.6897362819287 };
    private static readonly double[] W2 = { 0.6897362819287, 0.5, 0.3102637180713 };

    internal sealed class Node
    {
        public int C0 = -1, C1 = -1, Depth;
        public double X0, Y0, X1, Y1;
        public int[] Corners = null!;
    }

    internal readonly List<(double X, double Y)> Pts = new();
    internal readonly List<Node> Nodes = new();

    private KdTree(double x0, double y0, double x1, double y1)
    {
        Pts.Add((x0, y0)); Pts.Add((x0, y1)); Pts.Add((x1, y1)); Pts.Add((x1, y0));
        Nodes.Add(new Node { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Corners = new[] { 0, 1, 2, 3 } });
    }

    private int Split(int ni, int dir, double m)
    {
        var n = Nodes[ni];
        if (dir == 0)
        {
            if (Math.Abs(m - n.X0) <= Eps || Math.Abs(m - n.X1) <= Eps) return -1;
        }
        else if (Math.Abs(m - n.Y0) <= Eps || Math.Abs(m - n.Y1) <= Eps) return -1;
        int k = Nodes.Count;
        n.C0 = k; n.C1 = k + 1;
        int p = Pts.Count;
        var cn = n.Corners;
        if (dir == 1)
        {
            Pts.Add((n.X1, m)); Pts.Add((n.X0, m));
            Nodes.Add(new Node { X0 = n.X0, Y0 = n.Y0, X1 = n.X1, Y1 = m, Depth = n.Depth + 1, Corners = new[] { cn[0], p + 1, p, cn[3] } });
            Nodes.Add(new Node { X0 = n.X0, Y0 = m, X1 = n.X1, Y1 = n.Y1, Depth = n.Depth + 1, Corners = new[] { p + 1, cn[1], cn[2], p } });
        }
        else
        {
            Pts.Add((m, n.Y1)); Pts.Add((m, n.Y0));
            Nodes.Add(new Node { X0 = n.X0, Y0 = n.Y0, X1 = m, Y1 = n.Y1, Depth = n.Depth + 1, Corners = new[] { cn[0], cn[1], p, p + 1 } });
            Nodes.Add(new Node { X0 = m, Y0 = n.Y0, X1 = n.X1, Y1 = n.Y1, Depth = n.Depth + 1, Corners = new[] { p + 1, p, cn[2], cn[3] } });
        }
        return k;
    }

    private static double D2(double[] a, double[] b)
    {
        double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
        return dx * dx + dy * dy + dz * dz;
    }

    private static double[] Closest(double[] a, double[] b, double[] p)
    {
        double abx = b[0] - a[0], aby = b[1] - a[1], abz = b[2] - a[2];
        double l2 = abx * abx + aby * aby + abz * abz;
        if (l2 == 0) return a;
        double t = ((p[0] - a[0]) * abx + (p[1] - a[1]) * aby + (p[2] - a[2]) * abz) / l2;
        t = Math.Max(0.0, Math.Min(1.0, t));
        return new[] { a[0] + t * abx, a[1] + t * aby, a[2] + t * abz };
    }

    private static double[] Mid(double[] a, double[] b) => new[] { (a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5, (a[2] + b[2]) * 0.5 };

    /// <summary>-1: no split, 0: split x, 1: split y.</summary>
    private static int NeedSplit(Func<double, double, double[]> s, double x0, double x1, double y0, double y1, double st, double mfl)
    {
        double tol2 = st * st;
        double xm = (x0 + x1) * 0.5, ym = (y0 + y1) * 0.5;
        var mb = s(xm, y0); var ml = s(x0, ym); var mr = s(x1, ym); var mt = s(xm, y1);
        double su = 0.0, sv = 0.0;
        for (int i = 0; i < 3; i++)
        {
            double w1 = W1[i], w2 = W2[i];
            var q = new[] { ml[0] * w1 + mr[0] * w2, ml[1] * w1 + mr[1] * w2, ml[2] * w1 + mr[2] * w2 };
            double a = D2(q, s(x0 * w1 + w2 * x1, ym));
            var q2 = new[] { mb[0] * w1 + mt[0] * w2, mb[1] * w1 + mt[1] * w2, mb[2] * w1 + mt[2] * w2 };
            double b = D2(q2, s(xm, y0 * w1 + w2 * y1));
            if (i > 0) { a = Math.Max(a, su); b = Math.Max(b, sv); }
            su = a; sv = b;
        }
        if (tol2 < su || tol2 < sv) return su <= sv ? 1 : 0;
        var s00 = s(x0, y0); var s01 = s(x0, y1); var s10 = s(x1, y0); var s11 = s(x1, y1);
        var m1 = Mid(s00, s11); var m2 = Mid(s01, s10);
        double d1 = Math.Min(D2(Closest(mb, mt, m1), m1), D2(Closest(ml, mr, m1), m1));
        double d2 = Math.Min(D2(Closest(mb, mt, m2), m2), D2(Closest(ml, mr, m2), m2));
        if (tol2 * 100.0 < d1 || tol2 * 100.0 < d2) return d1 <= d2 ? 1 : 0;
        double m2f = mfl * mfl;
        if (D2(s11, s01) > m2f) return 0;
        if (D2(s10, s00) > m2f) return 0;
        if (D2(s11, s10) > m2f) return 1;
        if (D2(s01, s00) > m2f) return 1;
        return -1; // the normal tolerance is 360 degrees: never splits
    }

    internal static KdTree Build(Func<double, double, double[]> s, double x0, double y0, double x1, double y1, double st, double mfl)
    {
        var t = new KdTree(x0, y0, x1, y1);
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int ni = stack.Pop();
            var n = t.Nodes[ni];
            if (n.Depth > MaxDepth) continue;
            int r = NeedSplit(s, n.X0, n.X1, n.Y0, n.Y1, st, mfl);
            if (r < 0) continue;
            double m = r == 0 ? (n.X0 + n.X1) * 0.5 : (n.Y0 + n.Y1) * 0.5;
            int k = t.Split(ni, r, m);
            if (k >= 0) { stack.Push(k); stack.Push(k + 1); }
        }
        return t;
    }
}
