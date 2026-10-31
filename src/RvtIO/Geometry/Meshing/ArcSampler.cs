namespace RvtIO.Geometry.Meshing;

/// <summary>The sampler of a circular edge, shared by every face kind that is bounded by arcs.</summary>
internal static class ArcSampler
{
    internal const double TwoPi = 2.0 * Math.PI;
    /// <summary>An arc whose curve interval is longer needs at least 6 samples instead of 4.</summary>
    internal const double LargeArc = 5.654866776461628;

    /// <summary>Number of equal intervals of an arc of radius R and the given parameter span.</summary>
    internal static int ArcCount(double radius, double span, double surfaceTolerance)
    {
        double eps = surfaceTolerance * 0.5;
        double v = eps / radius;
        double step;
        if (v > 0)
        {
            if (v >= 5e-6) step = 1.0 - v > -1.0 ? Math.Min(Math.PI / 4, 2.0 * Math.Acos(1.0 - v)) : Math.PI / 2;
            else step = 0.006283185307179587;
        }
        else step = 0.1963495408493621;
        return Math.Max((int)Math.Ceiling(Math.Abs(span) / step), 1);
    }

    /// <summary>
    /// Sampler parameters of an arc between <paramref name="from"/> and <paramref name="to"/>: from + step + step..., or an
    /// even division when fewer than 4 samples (6 for a large arc) result.
    /// </summary>
    internal static double[] ArcParams(double radius, double from, double to, double surfaceTolerance, bool large)
    {
        int n = ArcCount(radius, to - from, surfaceTolerance);
        int m = large ? 6 : 4;
        if (n + 1 < m)
        {
            var even = new double[m];
            for (int i = 0; i < m; i++) even[i] = i * (to - from) / (m - 1) + from;
            return even;
        }
        double step = (to - from) / n;
        var ts = new double[n + 1];
        double v = from;
        for (int i = 0; i <= n; i++) { ts[i] = v; v += step; }
        return ts;
    }

    /// <summary>Pieces longer than the maximum edge length (3D distance of consecutive samples) are halved until they fit:
    /// parameter (t[i-1] + t[i]) / 2 is inserted before sample i and the same pair is tested again.</summary>
    internal static List<double> BisectLong(IEnumerable<double> ts, Func<double, double[]> point, double maxEdgeLength)
    {
        if (!(maxEdgeLength > 0.0) || !double.IsFinite(maxEdgeLength)) throw new UnsupportedException("edge length limit");
        var list = ts.ToList();
        int i = 1, inserted = 0;
        while (i < list.Count)
        {
            var a = point(list[i - 1]); var b = point(list[i]);
            double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len <= maxEdgeLength) i++;
            else
            {
                // a non finite point or a curve that never gets short enough comes from a damaged file
                if (!double.IsFinite(len) || ++inserted > 1 << 20) throw new UnsupportedException("edge cannot be divided");
                list.Insert(i, (list[i] + list[i - 1]) * 0.5);
            }
        }
        return list;
    }

    private static double[] Cross(double[] a, double[] b) =>
        new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

    private static double[] Unit(double[] v)
    {
        double l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        return new[] { v[0] / l, v[1] / l, v[2] / l };
    }

    private static double AngleTo(double[] a, double[] b)
    {
        a = Unit(a); b = Unit(b);
        var c = Cross(a, b);
        return Math.Atan2(Math.Sqrt(c[0] * c[0] + c[1] * c[1] + c[2] * c[2]), a[0] * b[0] + a[1] * b[1] + a[2] * b[2]);
    }

    private static double AngleToN(double[] a, double[] b, double[] n)
    {
        double t = (b[0] * a[2] - a[0] * b[2]) * n[1] + (a[1] * b[2] - a[2] * b[1]) * n[0] + (a[0] * b[1] - b[0] * a[1]) * n[2];
        return t >= -1e-10 ? AngleTo(a, b) : TwoPi - AngleTo(a, b);
    }

    /// <summary>Shifts a parameter by whole periods into [lo, hi].</summary>
    internal static double WrapPeriod(double lo, double hi, double v, double period = TwoPi)
    {
        double v4 = v;
        if (lo > v4)
        {
            v4 = lo - Fmod(lo - v4, period);
            if (lo > v4) v4 += period;
        }
        if (v4 > hi)
        {
            v4 = Fmod(v4 - hi, period) + hi;
            if (v4 > hi) v4 -= period;
        }
        if (lo > v4 && lo - v4 > Math.Abs(hi - v4 - period)) v4 += period;
        return v4;
    }

    private static double Fmod(double a, double b) => a % b; // C fmod: sign of the dividend, like C#

    internal static double ParamOf(Arc arc, double[] p)
    {
        var per = Cross(arc.Normal, arc.Ref);
        var n = Cross(arc.Ref, per);
        double[] d = { arc.Center[0] - p[0], arc.Center[1] - p[1], arc.Center[2] - p[2] };
        double t = d[1] * n[1] + d[0] * n[0] + d[2] * n[2];
        double[] v16 = { t * n[0] - d[0], t * n[1] - d[1], t * n[2] - d[2] };
        return WrapPeriod(arc.Start, arc.End, AngleToN(arc.Ref, v16, n));
    }

    /// <summary>The parameter range handed to the sampler for an arc edge running from vertex p1 to vertex p2: paramOf of both
    /// vertices unwrapped so that to &gt; from, from &gt;= 0 and to &lt;= 2 pi.</summary>
    internal static (double From, double To) RendererInterval(Arc arc, double[] p1, double[] p2)
    {
        return NormalizeRange(ParamOf(arc, p1), ParamOf(arc, p2));
    }

    /// <summary>Unwraps a pair of angles so that to &gt; from, from &gt;= 0 and to &lt;= 2 pi.</summary>
    internal static (double From, double To) NormalizeRange(double a, double b)
    {
        if (!double.IsFinite(a) || !double.IsFinite(b) || Math.Abs(a) > 1e6 || Math.Abs(b) > 1e6) throw new UnsupportedException("arc parameter");
        while (a >= b) b += TwoPi;
        if (a < 0.0)
            do { a += TwoPi; b += TwoPi; } while (a < 0.0);
        if (b > TwoPi)
            do { a -= TwoPi; b -= TwoPi; } while (b > TwoPi);
        return (a, b);
    }

    /// <summary>Sample points of a circular coedge for a planar face: the arc sampler between the edge vertices
    /// (<paramref name="edgeFirst"/> to <paramref name="edgeLast"/>, the direction of the curve), listed in the walking direction of
    /// the loop and with the two ends pinned to the B-rep vertices of the coedge.</summary>
    internal static double[][] SampleCoedge(Arc arc, double[] edgeFirst, double[] edgeLast, double[] coedgeStart, double[] coedgeEnd,
        double lod, double extentsDiagonal)
    {
        var pts = Sample(arc, edgeFirst, edgeLast, arc.Start, arc.End, Tolerances.SurfaceTolerance(lod),
            Tolerances.MaxEdgeLength(extentsDiagonal, lod));
        if (Distance(pts[^1], coedgeStart) < Distance(pts[0], coedgeStart)) Array.Reverse(pts);
        pts[0] = (double[])coedgeStart.Clone();
        pts[^1] = (double[])coedgeEnd.Clone();
        return pts;
    }

    internal static double[][] SampleNurbsCoedge(Nurbs nb, double[] coedgeStart, double[] coedgeEnd, double lod, double extentsDiagonal)
    {
        double a = nb.Knots[nb.Degree], b = nb.Knots[nb.ControlPoints.Length];
        var cps = nb.ControlPoints;
        bool closed = Distance(cps[0], cps[^1]) <= 1e-4;
        var list = NurbsEval.Tessellate3d(nb, a, b, Tolerances.SurfaceTolerance(lod) * 0.5);
        int m = closed ? 6 : 4;
        if (list.Count < m)
        {
            list.Clear();
            for (int i = 0; i < m; i++) list.Add(i * (b - a) / (m - 1) + a);
        }
        var ts = BisectLong(list, t => NurbsEval.Point3d(nb, t), Tolerances.MaxEdgeLength(extentsDiagonal, lod));
        var pts = ts.Select(t => NurbsEval.Point3d(nb, t)).ToArray();
        if (Distance(pts[^1], coedgeStart) < Distance(pts[0], coedgeStart)) Array.Reverse(pts);
        pts[0] = (double[])coedgeStart.Clone();
        pts[^1] = (double[])coedgeEnd.Clone();
        return pts;
    }

    private static double Distance(double[] a, double[] b) =>
        Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));

    /// <summary>3D sample points of an arc edge from its start vertex to its end vertex (along the curve direction), including the
    /// maximum edge length bisection. The end points are the curve points of the end parameters, not the vertices.</summary>
    internal static double[][] Sample(Arc arc, double[] startVertex, double[] endVertex, double curveLo, double curveHi,
        double surfaceTolerance, double maxEdgeLength)
    {
        var per = Cross(arc.Normal, arc.Ref);
        double[] Point(double t)
        {
            double ct = Math.Cos(t) * arc.Radius, sn = Math.Sin(t) * arc.Radius;
            return new[]
            {
                arc.Center[0] + ct * arc.Ref[0] + sn * per[0],
                arc.Center[1] + ct * arc.Ref[1] + sn * per[1],
                arc.Center[2] + ct * arc.Ref[2] + sn * per[2],
            };
        }
        var (from, to) = RendererInterval(arc, startVertex, endVertex);
        var ts = ArcParams(arc.Radius, from, to, surfaceTolerance, curveHi - curveLo > LargeArc);
        return BisectLong(ts, Point, maxEdgeLength).Select(Point).ToArray();
    }
}
