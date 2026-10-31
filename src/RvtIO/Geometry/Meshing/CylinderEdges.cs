namespace RvtIO.Geometry.Meshing;

internal sealed class UnsupportedException : Exception
{
    public UnsupportedException(string message) : base(message) { }
}

/// <summary>The cylinder frame in node coordinates: x = axial / UScale, y = angle.</summary>
internal sealed class CylinderFrame
{
    private readonly double r, us, sinA, cosA;
    private readonly bool cone;
    private readonly double[] o, a, e1, e2;

    internal CylinderFrame(CylinderSurface cyl)
    {
        r = cyl.Radius; o = cyl.Origin; a = cyl.Axis; e1 = cyl.Ref; us = cyl.UScale;
        cone = cyl.ConeHalfAngle != 0.0; sinA = Math.Sin(cyl.ConeHalfAngle); cosA = Math.Cos(cyl.ConeHalfAngle);
        e2 = new[] { a[1] * e1[2] - a[2] * e1[1], a[2] * e1[0] - a[0] * e1[2], a[0] * e1[1] - a[1] * e1[0] };
    }

    /// <summary>Surface point of the node coordinates.</summary>
    internal double[] S(double x, double y)
    {
        double h = x * us, rr = r;
        if (cone) { double slant = h; h = slant * cosA; rr = slant * sinA; }
        double c = Math.Cos(y) * rr, sn = Math.Sin(y) * rr;
        return new[]
        {
            o[0] + c * e1[0] + sn * e2[0] + h * a[0],
            o[1] + c * e1[1] + sn * e2[1] + h * a[1],
            o[2] + c * e1[2] + sn * e2[2] + h * a[2],
        };
    }

    internal (double X, double Y) Uv(double[] p, double ylo, double yhi)
    {
        double dx = p[0] - o[0], dy = p[1] - o[1], dz = p[2] - o[2];
        double num = (e1[0] * a[2] - a[0] * e1[2]) * dy + (e1[2] * a[1] - e1[1] * a[2]) * dx + (a[0] * e1[1] - e1[0] * a[1]) * dz;
        double den = dx * e1[0] + dy * e1[1] + dz * e1[2];
        double ang = ArcSampler.WrapPeriod(ylo, yhi, Math.Atan2(num, den));
        double axial = dx * a[0] + dy * a[1] + dz * a[2];
        return ((cone ? axial / cosA : axial) / us, ang);
    }
}

/// <summary>One sample of a loop edge: its parameter point and the 3D point of the mesh vertex (the point of the 3D edge curve).</summary>
internal readonly record struct CylinderSample((double X, double Y) Uv, double[] P3);

/// <summary>
/// The samples of one coedge of a cylinder face: the parameters of the 3D edge curve (line: the two ends, arc: the shared arc
/// sampler, NURBS: the chord-test bisection) with the pieces longer than the maximum edge length halved, and their parameter
/// points: the parameter curve when it reproduces the 3D edge, else the cylinder paramOf of the 3D point.
/// </summary>
internal sealed class CylinderEdge
{
    private const double SnapTolerance = 1e-10;

    private readonly MeshCoedge coedge;
    private readonly CylinderFrame frame;
    private readonly (double X0, double Y0, double X1, double Y1) env;
    private readonly double angleLo, angleHi;
    private readonly List<double> ts;
    private readonly Func<double, double[]> point;
    private readonly double period;
    private readonly Nurbs? pcurve;
    private readonly double pcLo, pcHi;

    internal CylinderEdge(MeshCoedge e, double surfaceTolerance, double maxEdgeLength, CylinderFrame frame,
        (double X0, double Y0, double X1, double Y1) env, double angleLo, double angleHi)
    {
        coedge = e; this.frame = frame; this.env = env; this.angleLo = angleLo; this.angleHi = angleHi;
        var c = e.Curve ?? throw new UnsupportedException("coedge without curve");
        var v1 = e.V1 ?? throw new UnsupportedException("coedge without vertices");
        var v2 = e.V2 ?? throw new UnsupportedException("coedge without vertices");
        if (e.ParamCurve?.Nurbs is { } pn)
        {
            if (pn.Weights != null) throw new UnsupportedException("rational parameter curve");
            pcurve = pn; pcLo = e.ParamCurve.Lo; pcHi = e.ParamCurve.Hi;
        }
        bool o2c = e.EdgeOrientToCurve;
        var vs = o2c ? v1 : v2; var ve = o2c ? v2 : v1;
        double[] raw;
        switch (c.Kind)
        {
            case CurveKind.Line:
            {
                raw = o2c ? new[] { e.ParamV1, e.ParamV2 } : new[] { e.ParamV2, e.ParamV1 };
                var p0 = c.P0 ?? throw new UnsupportedException("line without ends"); var p1 = c.P1!;
                double lo = c.Lo, hi = c.Hi;
                point = t =>
                {
                    double u = (t - lo) / (hi - lo);
                    return new[] { p0[0] + (p1[0] - p0[0]) * u, p0[1] + (p1[1] - p0[1]) * u, p0[2] + (p1[2] - p0[2]) * u };
                };
                period = 0.0;
                break;
            }
            case CurveKind.Arc:
            {
                var arc = c.Arc ?? throw new UnsupportedException("arc without parameters");
                var per = new[]
                {
                    arc.Normal[1] * arc.Ref[2] - arc.Normal[2] * arc.Ref[1],
                    arc.Normal[2] * arc.Ref[0] - arc.Normal[0] * arc.Ref[2],
                    arc.Normal[0] * arc.Ref[1] - arc.Normal[1] * arc.Ref[0],
                };
                var (from, to) = arc.EdgeRange ?? ArcSampler.RendererInterval(arc, vs, ve);
                raw = ArcSampler.ArcParams(arc.Radius, from, to, surfaceTolerance, c.Hi - c.Lo > ArcSampler.LargeArc);
                point = t =>
                {
                    double ct = Math.Cos(t) * arc.Radius, sn = Math.Sin(t) * arc.Radius;
                    return new[]
                    {
                        arc.Center[0] + ct * arc.Ref[0] + sn * per[0],
                        arc.Center[1] + ct * arc.Ref[1] + sn * per[1],
                        arc.Center[2] + ct * arc.Ref[2] + sn * per[2],
                    };
                };
                period = ArcSampler.TwoPi;
                break;
            }
            case CurveKind.Nurbs:
            {
                var nb = c.Nurbs ?? throw new UnsupportedException("nurbs edge without parameters");
                if (nb.Weights != null) throw new UnsupportedException("rational edge curve");
                double a = o2c ? e.ParamV1 : e.ParamV2, b = o2c ? e.ParamV2 : e.ParamV1;
                if (!(a < b)) throw new UnsupportedException("closed or periodic nurbs edge");
                var cps = nb.ControlPoints;
                double dx = cps[0][0] - cps[^1][0], dy = cps[0][1] - cps[^1][1], dz = cps[0][2] - cps[^1][2];
                bool closed = Math.Sqrt(dx * dx + dy * dy + dz * dz) <= 1e-4;
                var list = NurbsEval.Tessellate3d(nb, a, b, surfaceTolerance * 0.5);
                int m = closed ? 6 : 4;
                if (list.Count < m)
                {
                    list.Clear();
                    for (int i = 0; i < m; i++) list.Add(i * (b - a) / (m - 1) + a);
                }
                raw = list.ToArray();
                period = nb.Knots[cps.Length] - nb.Knots[nb.Degree];
                point = t => NurbsEval.Point3d(nb, t);
                break;
            }
            default:
                throw new UnsupportedException("edge curve");
        }
        ts = ArcSampler.BisectLong(raw, point, maxEdgeLength);
    }

    private static double Dist(double[] p, double[] q)
    {
        double dx = p[0] - q[0], dy = p[1] - q[1], dz = p[2] - q[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>A parameter point must lie in the surface envelope (1e-8 tolerance; no check in a closed direction).</summary>
    private bool InsideEnvelope((double X, double Y) uv)
    {
        if (!(env.X0 - 1e-8 <= uv.X && uv.X <= env.X1 + 1e-8)) return false;
        if (env.Y1 - env.Y0 >= ArcSampler.TwoPi - 1e-9) return true;
        return env.Y0 - 1e-8 <= uv.Y && uv.Y <= env.Y1 + 1e-8;
    }

    /// <summary>The parameter curve is only used when it reproduces the 3D edge: the parameters at 20% and 80% of the edge range
    /// (shifted into the curve interval by whole periods) and the two end parameters must lie in the surface envelope, their
    /// surface images must be closer to the 3D curve points of the same parameters than to the swapped ones, and within
    /// min(0.5, chord / 10).</summary>
    internal bool PcurveValid()
    {
        if (pcurve == null) return false;
        double a = ts[0], b = ts[^1];
        double a5 = (b - a) * 0.2;
        double ta = a + a5, tb = b - a5;
        var (ta2, tb2) = ShiftRange(pcLo, pcHi, period, ta, tb, a5);
        if (!(ta2 > pcLo + a5 - 1e-7 && pcHi - a5 + 1e-7 > tb2)) return false;
        var (va, vb) = ShiftRange(pcLo, pcHi, period, a, b, 0.0);
        var uv1 = NurbsEval.Point2d(pcurve, ta2); var uv2 = NurbsEval.Point2d(pcurve, tb2);
        foreach (var q in new[] { uv1, uv2, NurbsEval.Point2d(pcurve, va), NurbsEval.Point2d(pcurve, vb) })
            if (!InsideEnvelope(q)) return false;
        var s1 = frame.S(uv1.X, uv1.Y); var s2 = frame.S(uv2.X, uv2.Y);
        var ca = point(ta); var cb = point(tb);
        double e1 = Dist(ca, s1), x1 = Dist(ca, s2), x2 = Dist(cb, s1), e2 = Dist(cb, s2);
        double lim = Dist(ca, cb) / 10.0;
        double limit = lim > 1e-10 || lim < -1e-10 ? Math.Min(0.5, lim) : 0.5;
        return x1 > e1 && x2 > e2 && e1 <= limit && e2 <= limit;
    }

    /// <summary>Shifts [from, to] by multiples of the period into the parameter curve interval [lo, hi] (tolerance 1e-7; a5 is a
    /// margin: lower bound lo + a5, upper bound hi - a5).</summary>
    private static (double From, double To) ShiftRange(double lo, double hi, double period, double from, double to, double a5)
    {
        if (-1e-7 <= period && period <= 1e-7) return (from, to);
        double f0 = from;
        double v9 = hi - a5;
        double v10 = lo + a5 - 1e-7;
        double v13 = v10 + period;
        double v15, v17, v21;
        if (v10 <= from) v15 = to;
        else
        {
            double v14 = (v10 - from) % period;
            from = v13 - v14;
            v15 = from + to - f0;
            to = v15;
        }
        if (v10 <= v15) v17 = from;
        else
        {
            double v16 = v13 - (v10 - v15) % period;
            v17 = v16 - (v15 - from);
            to = v16;
            from = v17;
        }
        double v18 = v9 + 1e-7;
        double v19 = v18 - period;
        if (v17 <= v18) v21 = to;
        else
        {
            double v20 = (v17 - v18) % period + v19;
            v21 = v20 + to - v17;
            from = v20; to = v21;
        }
        if (v21 > v18)
        {
            double v22 = v21 - from;
            double v23 = (v21 - v18) % period + v19;
            to = v23; from = v23 - v22;
        }
        return (from, to);
    }

    private static (double X, double Y) Snap((double X, double Y) uv, (double X0, double Y0, double X1, double Y1) box)
    {
        double x = uv.X, y = uv.Y;
        if (Math.Abs(x - box.X0) <= SnapTolerance) x = box.X0;
        else if (Math.Abs(x - box.X1) <= SnapTolerance) x = box.X1;
        if (Math.Abs(y - box.Y0) <= SnapTolerance) y = box.Y0;
        else if (Math.Abs(y - box.Y1) <= SnapTolerance) y = box.Y1;
        return (x, y);
    }

    /// <summary>Samples along the curve direction. <paramref name="usePcurve"/> must only be set when <see cref="PcurveValid"/>.</summary>
    internal CylinderSample[] Samples(bool usePcurve)
    {
        var result = new CylinderSample[ts.Count];
        double off = 0.0;
        if (usePcurve) off = ts[0] - ShiftRange(pcLo, pcHi, period, ts[0], ts[^1], 0.0).From;
        for (int i = 0; i < ts.Count; i++)
        {
            var p3 = point(ts[i]);
            result[i] = new CylinderSample(usePcurve ? Snap(NurbsEval.Point2d(pcurve!, ts[i] - off), env) : frame.Uv(p3, angleLo, angleHi), p3);
        }
        return result;
    }
}
