namespace RvtIO.Geometry.Meshing;

internal static class NurbsEval
{
    private const double WLo = 0.3102637;
    private const double WHi = 0.6897363000000001;
    /// <summary>Knot tolerance the span search adds to the parameter.</summary>
    private const double KnotTolerance = 1e-9;

    /// <summary>Number of interior knots not above t + tolerance, plus the degree.</summary>
    private static int FindSpan(double[] u, int p, double t)
    {
        int count = 0;
        double tt = t + KnotTolerance;
        for (int i = p + 1; i < u.Length - (p + 1); i++)
            if (u[i] <= tt) count++;
        return count + p;
    }

    private static double[] Basis(double[] u, int p, int span, double t)
    {
        var n = new double[p + 1];
        n[p] = 1.0;
        int k = span - p;
        for (int l = 0; l < p; l++)
            for (int j = p - l - 1; j < p; j++)
            {
                double left = u[k + j + 1], right = u[k + j + 2 + l];
                double inv = 1.0 / (right - left);
                double w = (t - left) * inv;
                double n1 = n[j + 1];
                n[j] = n1 * (1.0 - w) + n[j];
                n[j + 1] = n1 * w;
            }
        return n;
    }

    /// <summary>Point of a non rational 2D B-spline; the parameter is clamped to [U[p], U[n]].</summary>
    internal static (double X, double Y) Point2d(Nurbs nb, double t)
    {
        var u = nb.Knots; var cps = nb.ControlPoints; int p = nb.Degree;
        double lo = u[p], hi = u[cps.Length];
        if (t < lo) t = lo; else if (t > hi) t = hi;
        int span = FindSpan(u, p, t);
        var n = Basis(u, p, span, t);
        double x = 0.0, y = 0.0;
        for (int j = 0; j <= p; j++)
        {
            x += n[j] * cps[span - p + j][0];
            y += n[j] * cps[span - p + j][1];
        }
        return (x, y);
    }

    private static int Span3d(double[] u, int p, int n, double t)
    {
        if (t >= u[n]) return n - 1;
        if (t <= u[p]) return p;
        int lo = p, hi = n, mid = (lo + hi) / 2;
        while (t < u[mid] || t >= u[mid + 1])
        {
            if (t < u[mid]) hi = mid; else lo = mid;
            mid = (lo + hi) / 2;
        }
        return mid;
    }

    /// <summary>Point of a non rational 3D B-spline (used for chord tests and as the sample position).</summary>
    internal static double[] Point3d(Nurbs nb, double t)
    {
        var u = nb.Knots; var cps = nb.ControlPoints; int p = nb.Degree, n = cps.Length;
        t = Math.Min(Math.Max(t, u[p]), u[n]);
        int k = Span3d(u, p, n, t);
        var basis = new double[p + 1]; var left = new double[p + 1]; var right = new double[p + 1];
        basis[0] = 1.0;
        for (int j = 1; j <= p; j++)
        {
            left[j] = t - u[k + 1 - j]; right[j] = u[k + j] - t;
            double saved = 0.0;
            for (int r = 0; r < j; r++)
            {
                double tmp = basis[r] / (right[r + 1] + left[j - r]);
                basis[r] = saved + right[r + 1] * tmp;
                saved = left[j - r] * tmp;
            }
            basis[j] = saved;
        }
        double x = 0.0, y = 0.0, z = 0.0, w = 0.0;
        for (int j = 0; j <= p; j++)
        {
            var q = cps[k - p + j];
            x += basis[j] * q[0]; y += basis[j] * q[1]; z += basis[j] * q[2]; w += basis[j];
        }
        return new[] { x / w, y / w, z / w };
    }

    private static bool Near(double[] a, double[] b, double tol)
    {
        double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= tol;
    }

    /// <summary>Sample parameters [a,..., b] of a 3D curve: bisect while the chord test fails (depth at most 20).</summary>
    internal static List<double> Tessellate3d(Nurbs nb, double a, double b, double eps)
    {
        var output = new List<double> { a };
        void Rec(double t0, double t1, int depth)
        {
            if (depth > 20) return;
            var p0 = Point3d(nb, t0); var p1 = Point3d(nb, t1);
            double tm = (t0 + t1) * 0.5;
            var pm = Point3d(nb, tm);
            var cm = new[] { (p0[0] + p1[0]) * 0.5, (p0[1] + p1[1]) * 0.5, (p0[2] + p1[2]) * 0.5 };
            bool ok = Near(pm, cm, eps);
            if (ok)
            {
                var pa = Point3d(nb, t0 * WLo + t1 * WHi);
                var ca = new[] { p1[0] * WHi + p0[0] * WLo, p1[1] * WHi + p0[1] * WLo, p1[2] * WHi + p0[2] * WLo };
                ok = Near(pa, ca, eps);
                if (ok)
                {
                    var pb = Point3d(nb, t0 * WHi + t1 * WLo);
                    var cb = new[] { p0[0] * WHi + p1[0] * WLo, p0[1] * WHi + p1[1] * WLo, p0[2] * WHi + p1[2] * WLo };
                    ok = Near(pb, cb, eps);
                }
            }
            if (!ok)
            {
                Rec(t0, tm, depth + 1);
                output.Add(tm);
                Rec(tm, t1, depth + 1);
            }
        }
        Rec(a, b, 0);
        output.Add(b);
        return output;
    }
}
