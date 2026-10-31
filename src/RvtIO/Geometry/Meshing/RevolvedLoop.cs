namespace RvtIO.Geometry.Meshing;

/// <summary>
/// The edge uv curve of a revolved face: a cubic B-spline that interpolates the uv samples stored with the edge (natural end
/// conditions, chord length knots), evaluated like the mesher's light NURBS curve (Cox-de Boor in place, non-rational).
/// </summary>
internal sealed class RevolvedPCurve
{
    private const double TolB = 9.999999999999999e-12;
    private readonly double[] k;
    private readonly (double X, double Y)[] c;

    private RevolvedPCurve(double[] knots, (double X, double Y)[] ctrl) { k = knots; c = ctrl; }

    internal double Lo => k[0];
    internal double Hi => k[^1];

    private static double N(double[] kn, int i, int p, double t)
    {
        if (p == 0)
            return (t == kn[i + 1] && t == kn[^1]) || (t >= kn[i] && kn[i + 1] > t) ? 1.0 : 0.0;
        double v13 = 0.0;
        if (TolB < Math.Abs(kn[i + p] - kn[i])) v13 = (t - kn[i]) * N(kn, i, p - 1, t) / (kn[i + p] - kn[i]);
        double v12 = 0.0;
        double d = kn[i + p + 1] - kn[i + 1];
        if (TolB < Math.Abs(d)) v12 = N(kn, i + 1, p - 1, t) * (kn[i + p + 1] - t) / d;
        return v12 + v13;
    }

    private static double D(double[] kn, int i, int p, double t)
    {
        if (p == 0) return 0.0;
        double v12 = 0.0;
        if (TolB < Math.Abs(kn[i + p] - kn[i]))
        {
            double v13 = (t - kn[i]) * D(kn, i, p - 1, t);
            v12 = (N(kn, i, p - 1, t) + v13) / (kn[i + p] - kn[i]);
        }
        double v11 = 0.0;
        double d = kn[i + p + 1] - kn[i + 1];
        if (TolB < Math.Abs(d))
        {
            double v14 = D(kn, i + 1, p - 1, t);
            v11 = ((kn[i + p + 1] - t) * v14 - N(kn, i + 1, p - 1, t)) / d;
        }
        return v11 + v12;
    }

    private static double S(double[] kn, int i, int p, double t)
    {
        if (p == 1) return 0.0;
        double v12 = 0.0;
        if (TolB < Math.Abs(kn[i + p] - kn[i]))
        {
            double v13 = (t - kn[i]) * S(kn, i, p - 1, t);
            v12 = (D(kn, i, p - 1, t) * 2.0 + v13) / (kn[i + p] - kn[i]);
        }
        double v11 = 0.0;
        double d = kn[i + p + 1] - kn[i + 1];
        if (TolB < Math.Abs(d))
        {
            double v14 = S(kn, i + 1, p - 1, t);
            v11 = ((kn[i + p + 1] - t) * v14 - D(kn, i + 1, p - 1, t) * 2.0) / d;
        }
        return v11 + v12;
    }

    /// <summary>The interpolating spline of the fit points; null when the system is singular or fewer than 2 points.</summary>
    internal static RevolvedPCurve? Fit(IReadOnlyList<(double X, double Y)> pts)
    {
        int n = pts.Count;
        if (n < 2) return null;
        var kn = new double[n + 6];
        double acc = 0.0;
        for (int i = 0; i < n - 1; i++)
        {
            double dx = pts[i].X - pts[i + 1].X, dy = pts[i].Y - pts[i + 1].Y;
            double d2 = dx * dx + dy * dy + 0.0;
            acc = Math.Sqrt(d2) + acc;
            kn[4 + i] = acc;
        }
        double last = kn[n + 2];
        kn[n + 3] = kn[n + 4] = kn[n + 5] = last;
        int nn = n + 2;
        var flat = new double[3 * nn - 2];
        flat[0] = 1.0;
        flat[1] = 0.0;
        for (int i = 0; i < 3; i++) flat[2 + i] = S(kn, i, 3, kn[0]);
        for (int r = 2; r < n; r++)
        {
            int i = r - 1;
            double t = kn[i + 3];
            for (int j = i; j < i + 3; j++) flat[3 * r - 1 + (j - i)] = N(kn, j, 3, t);
        }
        double tl = kn[n + 2];
        for (int j = n - 1; j < n + 2; j++) flat[3 * n - 1 + (j - (n - 1))] = S(kn, j, 3, tl);
        flat[3 * n + 2] = 0.0;
        flat[3 * n + 3] = 1.0;
        var rhs = new (double X, double Y)[nn];
        rhs[0] = pts[0];
        rhs[1] = (0.0, 0.0);
        for (int i = 1; i < n - 1; i++) rhs[i + 1] = pts[i];
        rhs[n] = (0.0, 0.0);
        rhs[n + 1] = pts[n - 1];
        var gamma = new double[nn - 1];
        var y = new (double X, double Y)[nn];
        double a00 = flat[0];
        if (Math.Abs(a00) <= 1e-50) return null;
        gamma[0] = -flat[1] / a00;
        y[0] = (rhs[0].X / a00, rhs[0].Y / a00);
        for (int i = 1; i < nn - 1; i++)
        {
            double sub = flat[3 * i - 1], diag = flat[3 * i], sup = flat[3 * i + 1];
            double den = sub * gamma[i - 1] + diag;
            if (Math.Abs(den) <= 1e-50) return null;
            gamma[i] = -sup / den;
            y[i] = ((rhs[i].X - sub * y[i - 1].X) / den + 0.0, (rhs[i].Y - sub * y[i - 1].Y) / den + 0.0);
        }
        double sb = flat[3 * nn - 4], dg = flat[3 * nn - 3];
        double dn = sb * gamma[nn - 2] + dg;
        if (Math.Abs(dn) <= 1e-50) return null;
        var x = new (double X, double Y)[nn];
        x[nn - 1] = ((rhs[nn - 1].X - sb * y[nn - 2].X) / dn + 0.0, (rhs[nn - 1].Y - sb * y[nn - 2].Y) / dn + 0.0);
        for (int i = nn - 2; i >= 0; i--) x[i] = (gamma[i] * x[i + 1].X + y[i].X, gamma[i] * x[i + 1].Y + y[i].Y);
        return new RevolvedPCurve(kn, x);
    }

    internal RevolvedPCurve Rescaled(double newStart, double newEnd)
    {
        int n = k.Length;
        var o = (double[])k.Clone();
        double total = k[n - 1] - k[0];
        double prevOld = k[0];
        o[0] = newStart;
        for (int i = 1; i < n; i++)
        {
            double d = k[i] - prevOld;
            prevOld = k[i];
            o[i] = d * (newEnd - newStart) / total + o[i - 1];
        }
        return new RevolvedPCurve(o, c);
    }

    /// <summary>The curve with its knots rescaled to [t0, t1] when they differ from the current ones by more than 1e-10.</summary>
    internal RevolvedPCurve OnInterval(double t0, double t1) =>
        Math.Abs(t0 - k[0]) > 1e-10 || Math.Abs(t1 - k[^1]) > 1e-10 ? Rescaled(t0, t1) : this;

    internal (double X, double Y) Eval(double t)
    {
        int deg = k.Length - c.Length - 1;
        int cnt = k.Length - 2 * (deg + 1), pos = deg + 1;
        double t2 = t + 1e-9;
        while (cnt > 0)
        {
            int half = cnt / 2;
            if (k[pos + half] <= t2) { pos += half + 1; cnt -= half + 1; }
            else cnt = half;
        }
        int span = pos - (deg + 1) + deg;
        var nb = new double[deg + 1];
        nb[deg] = 1.0;
        for (int j = 0; j < deg; j++)
            for (int idx = deg - j - 1; idx < deg; idx++)
            {
                double lo = k[span - deg + 1 + idx], hi = k[span - deg + 2 + j + idx];
                double inv = 1.0 / (hi - lo);
                double s = (t - lo) * inv;
                double n1 = nb[idx + 1];
                nb[idx] = n1 * (1.0 - s) + nb[idx];
                nb[idx + 1] = n1 * s;
            }
        double ax = 0.0, ay = 0.0;
        for (int i = 0; i <= deg; i++)
        {
            var p = c[span - deg + i];
            ax += p.X * nb[i];
            ay += p.Y * nb[i];
        }
        return (ax, ay);
    }
}
