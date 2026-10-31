namespace RvtIO.Geometry.Meshing;

/// <summary>A 3D point or vector.</summary>
internal readonly struct Pt3
{
    public readonly double X, Y, Z;
    public Pt3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public static Pt3 From(double[] a) => new(a[0], a[1], a[2]);
    public double[] ToArray() => new[] { X, Y, Z };
    public static Pt3 operator +(Pt3 a, Pt3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Pt3 operator -(Pt3 a, Pt3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Pt3 operator *(Pt3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Pt3 operator *(double s, Pt3 a) => new(s * a.X, s * a.Y, s * a.Z);
    public static Pt3 operator /(Pt3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public double Dot(Pt3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public double Norm => Math.Sqrt(X * X + Y * Y + Z * Z);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public static Pt3 Cross(Pt3 a, Pt3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}

internal sealed class FfNurbs
{
    private const double KnotTolerance = 1e-9;
    internal int Degree { get; }
    internal double[] Knots { get; }
    internal double[][] Cps { get; }
    private readonly double[]? weights;

    private FfNurbs(int degree, double[] knots, double[][] cps, double[]? weights)
    {
        Degree = degree; Knots = knots; Cps = cps; this.weights = weights;
    }

    /// <summary>null for malformed data and, unless <paramref name="allowRational"/>, for rational curves.</summary>
    internal static FfNurbs? Create(Nurbs? n, int dim, bool allowRational = false)
    {
        if (n == null || n.Degree < 1) return null;
        bool rational = n.Weights is { Length: > 0 };
        if (rational && (!allowRational || n.Weights!.Length != n.ControlPoints.Length || n.Weights.Any(w => !double.IsFinite(w) || w == 0.0))) return null;
        if (n.Knots.Length != n.ControlPoints.Length + n.Degree + 1 || n.ControlPoints.Length <= n.Degree) return null;
        if (n.ControlPoints.Any(c => c.Length != dim || c.Any(x => !double.IsFinite(x))) || n.Knots.Any(k => !double.IsFinite(k))) return null;
        for (int i = 1; i < n.Knots.Length; i++) if (n.Knots[i] < n.Knots[i - 1]) return null;
        return new FfNurbs(n.Degree, n.Knots, n.ControlPoints, rational ? n.Weights : null);
    }

    private static int FindSpan(double[] knots, int p, double u)
    {
        int b = p + 1, cnt = knots.Length - 2 * (p + 1), pos = b;
        double v = u + KnotTolerance;
        while (cnt > 0)
        {
            int half = cnt / 2;
            if (knots[pos + half] <= v) { pos += half + 1; cnt -= half + 1; }
            else cnt = half;
        }
        return pos - b + p;
    }

    private static double[] Basis(double[] knots, int p, int span, double u)
    {
        var m = new double[p + 1];
        m[p] = 1.0;
        for (int r = 0; r < p; r++)
            for (int i = 0; i <= r; i++)
            {
                int j = p - r - 1 + i;
                double khi = knots[span - p + j + r + 2], klo = knots[span - p + j + 1];
                if (khi == klo) throw new InvalidOperationException("zero knot interval");
                double alpha = (u - klo) * (1.0 / (khi - klo));
                double m1 = m[j + 1];
                m[j] = m1 * (1.0 - alpha) + m[j];
                m[j + 1] = m1 * alpha;
            }
        return m;
    }

    internal double[] Eval(double u)
    {
        if (weights != null) return EvalRational(u);
        double lo = Knots[Degree], hi = Knots[Knots.Length - Degree - 1];
        u = u < lo ? lo : u > hi ? hi : u;
        int span = FindSpan(Knots, Degree, u);
        var m = Basis(Knots, Degree, span, u);
        var acc = new double[Cps[0].Length];
        int b = span - Degree;
        for (int k = 0; k <= Degree; k++)
        {
            var c = Cps[b + k];
            for (int a = 0; a < acc.Length; a++) acc[a] = acc[a] + m[k] * c[a];
        }
        return acc;
    }

    private double[] EvalRational(double u)
    {
        int n = Cps.Length, i = FfSurfaces.FindSpan(Knots, Degree, n, u);
        var basis = FfSurfaces.Basis(Knots, Degree, i, u);
        var num = new double[Cps[0].Length];
        double den = 0.0;
        for (int k = 0; k <= Degree; k++)
        {
            double w = basis[k] * weights![i - Degree + k];
            var c = Cps[i - Degree + k];
            for (int a = 0; a < num.Length; a++) num[a] += w * c[a];
            den += w;
        }
        for (int a = 0; a < num.Length; a++) num[a] /= den;
        return num;
    }
}

/// <summary>An edge or profile curve of the freeform track, evaluated by its parameter t.</summary>
internal sealed class FfCurve
{
    internal CurveKind Kind { get; }
    internal double Lo { get; }
    internal double Hi { get; }
    internal double Radius { get; private init; }
    internal double Major { get; private init; }
    internal double Minor { get; private init; }
    internal FfNurbs? Nurbs { get; private init; }
    private Pt3 a, b, c, x, y;

    private FfCurve(CurveKind kind, double lo, double hi) { Kind = kind; Lo = lo; Hi = hi; }

    internal bool IsPeriodic => Kind is CurveKind.Arc or CurveKind.Ellipse;

    private static bool Ok(double[]? p) => p is { Length: 3 } && double.IsFinite(p[0]) && double.IsFinite(p[1]) && double.IsFinite(p[2]);

    /// <summary>null when the curve data are incomplete, not finite or rational (never guessed).</summary>
    internal static FfCurve? Create(MeshCurve? s)
    {
        if (s == null || !double.IsFinite(s.Lo) || !double.IsFinite(s.Hi)) return null;
        switch (s.Kind)
        {
            case CurveKind.Line:
                if (!Ok(s.P0) || !Ok(s.P1) || s.Hi == s.Lo) return null;
                return new FfCurve(s.Kind, s.Lo, s.Hi) { a = Pt3.From(s.P0!), b = Pt3.From(s.P1!) };
            case CurveKind.Arc:
            {
                var r = s.Arc;
                if (r == null || !Ok(r.Center) || !Ok(r.Normal) || !Ok(r.Ref) || !double.IsFinite(r.Radius)) return null;
                var refv = Pt3.From(r.Ref);
                return new FfCurve(s.Kind, s.Lo, s.Hi)
                {
                    Radius = r.Radius, c = Pt3.From(r.Center), x = refv * r.Radius, y = Pt3.Cross(Pt3.From(r.Normal), refv) * r.Radius,
                };
            }
            case CurveKind.Ellipse:
            {
                var e = s.Ellipse;
                if (e == null || !Ok(e.Center) || !Ok(e.MajorAxis) || !Ok(e.MinorAxis) || !double.IsFinite(e.MajorRadius) || !double.IsFinite(e.MinorRadius)) return null;
                return new FfCurve(s.Kind, s.Lo, s.Hi)
                {
                    Major = e.MajorRadius, Minor = e.MinorRadius, c = Pt3.From(e.Center),
                    x = Pt3.From(e.MajorAxis) * e.MajorRadius, y = Pt3.From(e.MinorAxis) * e.MinorRadius,
                };
            }
            case CurveKind.Nurbs:
            {
                var n = FfNurbs.Create(s.Nurbs, 3, allowRational: true);
                return n == null ? null : new FfCurve(s.Kind, s.Lo, s.Hi) { Nurbs = n };
            }
            default:
                return null;
        }
    }

    internal Pt3 At(double t)
    {
        switch (Kind)
        {
            case CurveKind.Arc:
            case CurveKind.Ellipse:
                return c + Math.Cos(t) * x + Math.Sin(t) * y;
            case CurveKind.Line:
                return a + (b - a) * ((t - Lo) / (Hi - Lo));
            default:
                var p = Nurbs!.Eval(t);
                return new Pt3(p[0], p[1], p[2]);
        }
    }

    /// <summary>Grows the box [mn, mx] by the points of the curve between the parameters lo and hi: the end points, for an arc or
    /// ellipse the axis extremes inside the range, for a NURBS curve 64 samples per knot span.</summary>
    internal void GrowBox(double lo, double hi, double[] mn, double[] mx)
    {
        void Add(Pt3 p)
        {
            mn[0] = Math.Min(mn[0], p.X); mn[1] = Math.Min(mn[1], p.Y); mn[2] = Math.Min(mn[2], p.Z);
            mx[0] = Math.Max(mx[0], p.X); mx[1] = Math.Max(mx[1], p.Y); mx[2] = Math.Max(mx[2], p.Z);
        }
        Add(At(lo)); Add(At(hi));
        if (Kind is CurveKind.Arc or CurveKind.Ellipse)
        {
            foreach (var (cx, cy) in new[] { (x.X, y.X), (x.Y, y.Y), (x.Z, y.Z) })
                foreach (double t0 in new[] { Math.Atan2(cy, cx), Math.Atan2(cy, cx) + Math.PI })
                {
                    double t = t0 - Math.Floor((t0 - lo) / ArcSampler.TwoPi) * ArcSampler.TwoPi; // the representative in [lo, lo + 2 pi)
                    if (t <= hi) Add(At(t));
                }
        }
        else if (Kind == CurveKind.Nurbs)
        {
            int spans = Nurbs!.Knots.Distinct().Count();
            for (int k = 1; k < 64 * spans; k++) Add(At(lo + (hi - lo) * k / (64 * spans)));
        }
    }

    /// <summary>Point at the fraction u of the parameter interval (the u parameter of a ruled surface profile).</summary>
    internal Pt3 AtUnit(double u) => At(Lo + (Hi - Lo) * u);

    internal bool IsClosed(double lo, double hi)
    {
        if (Kind is CurveKind.Arc or CurveKind.Ellipse) return hi - lo > 5.654866776461628;
        if (Kind == CurveKind.Nurbs)
        {
            var cps = Nurbs!.Cps;
            return (new Pt3(cps[0][0], cps[0][1], cps[0][2]) - new Pt3(cps[^1][0], cps[^1][1], cps[^1][2])).Norm <= 1e-10;
        }
        return false;
    }
}

/// <summary>The uv image of an edge on one face: the parameter curve evaluated at the parameter of the 3D edge curve.</summary>
internal sealed class FfParamCurve
{
    private readonly FfNurbs? nurbs;
    private readonly double[]? p0, p1;
    private readonly double lo, hi;
    private readonly bool periodic;

    private FfParamCurve(FfNurbs? n, double[]? a, double[]? b, double lo, double hi, bool periodic)
    {
        nurbs = n; p0 = a; p1 = b; this.lo = lo; this.hi = hi; this.periodic = periodic;
    }

    /// <summary>null when the data are incomplete or rational. <paramref name="periodic"/>: the edge curve is an arc or ellipse, so
    /// a parameter is shifted by whole turns into the interval of the parameter curve.</summary>
    internal static FfParamCurve? Create(ParamCurve? s, bool periodic)
    {
        if (s == null || !double.IsFinite(s.Lo) || !double.IsFinite(s.Hi)) return null;
        if (s.Nurbs != null)
        {
            var n = FfNurbs.Create(s.Nurbs, 2);
            return n == null ? null : new FfParamCurve(n, null, null, s.Lo, s.Hi, periodic);
        }
        bool Ok2(double[]? p) => p is { Length: 2 } && double.IsFinite(p[0]) && double.IsFinite(p[1]);
        if (!Ok2(s.P0) || !Ok2(s.P1) || s.Hi == s.Lo) return null;
        return new FfParamCurve(null, s.P0, s.P1, s.Lo, s.Hi, periodic);
    }

    internal (double U, double V) At(double t)
    {
        if (periodic)
        {
            while (t < lo - 1e-9) t += ArcSampler.TwoPi;
            while (t > hi + 1e-9) t -= ArcSampler.TwoPi;
        }
        t = Math.Min(Math.Max(t, lo), hi);
        if (nurbs != null)
        {
            var p = nurbs.Eval(t);
            return (p[0], p[1]);
        }
        double s = (t - lo) / (hi - lo);
        return (p0![0] + (p1![0] - p0[0]) * s, p0[1] + (p1[1] - p0[1]) * s);
    }
}

/// <summary>Surface evaluators of the freeform track.</summary>
internal static class FfSurfaces
{
    private static readonly bool MeshLightNurbs = Environment.GetEnvironmentVariable("RVTIO_X_NURBS") != "0";

    private static int LightSpan(double[] knots, int deg, double u)
    {
        int count = 0;
        double limit = u + 1e-10;
        for (int i = deg + 1; i < knots.Length - (deg + 1); i++)
            if (knots[i] <= limit) count++;
        return count + deg;
    }

    private static double[] LightBasis(double[] knots, int deg, int span, double u)
    {
        var n = new double[deg + 1];
        n[deg] = 1.0;
        int baseIndex = span - deg;
        for (int level = 0; level < deg; level++)
            for (int k = deg - level - 1; k < deg; k++)
            {
                double right = knots[baseIndex + k + 2 + level], left = knots[baseIndex + k + 1];
                double w = (u - left) * (1.0 / (right - left));
                double upper = n[k + 1];
                n[k] = upper * (1.0 - w) + n[k];
                n[k + 1] = upper * w;
            }
        return n;
    }

    internal static int FindSpan(double[] knots, int deg, int n, double u)
    {
        if (MeshLightNurbs) return LightSpan(knots, deg, u);
        if (u >= knots[n]) return n - 1;
        if (u <= knots[deg]) return deg;
        int lo = deg, hi = n;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (u < knots[mid]) hi = mid; else lo = mid;
        }
        return lo;
    }

    internal static double[] Basis(double[] knots, int deg, int i, double u)
    {
        if (MeshLightNurbs) return LightBasis(knots, deg, i, u);
        var n = new double[deg + 1];
        var left = new double[deg + 1];
        var right = new double[deg + 1];
        n[0] = 1.0;
        for (int j = 1; j <= deg; j++)
        {
            left[j] = u - knots[i + 1 - j];
            right[j] = knots[i + j] - u;
            double saved = 0.0;
            for (int r = 0; r < j; r++)
            {
                double d = right[r + 1] + left[j - r];
                double t = d != 0 ? n[r] / d : 0.0;
                n[r] = saved + right[r + 1] * t;
                saved = left[j - r] * t;
            }
            n[j] = saved;
        }
        return n;
    }

    /// <summary>Ruled surface evaluator, null when a profile is unusable.</summary>
    internal static Func<double, double, Pt3>? Ruled(RuledSurface s)
    {
        Func<double, Pt3>? Profile(MeshCurve? c, double[]? pt)
        {
            if (c == null) return pt is { Length: 3 } && double.IsFinite(pt[0]) && double.IsFinite(pt[1]) && double.IsFinite(pt[2]) ? (_ => Pt3.From(pt)) : null;
            var f = FfCurve.Create(c);
            return f == null ? null : f.AtUnit;
        }
        var c1 = Profile(s.Profile0, s.Point0);
        var c2 = Profile(s.Profile1, s.Point1);
        if (c1 == null || c2 == null) return null;
        return (u, v) => (1.0 - v) * c1(u) + v * c2(u);
    }

    /// <summary>NURBS surface evaluator (basis of the Piegl and Tiller algorithm), null for rational or malformed data.</summary>
    internal static Func<double, double, Pt3>? Nurbs(NurbsSurface s)
    {
        if (s.Weights is { Length: > 0 } || s.DegreeU < 1 || s.DegreeV < 1) return null;
        int nU = s.KnotsU.Length - s.DegreeU - 1, nV = s.KnotsV.Length - s.DegreeV - 1;
        if (nU <= s.DegreeU || nV <= s.DegreeV || s.ControlPoints.Length != nU * nV) return null;
        if (s.ControlPoints.Any(c => c.Length != 3 || c.Any(x => !double.IsFinite(x))) || s.KnotsU.Any(k => !double.IsFinite(k)) || s.KnotsV.Any(k => !double.IsFinite(k))) return null;
        var cps = s.ControlPoints;
        int degU = s.DegreeU, degV = s.DegreeV;
        double[] uk = s.KnotsU, vk = s.KnotsV;
        return (u, v) =>
        {
            int iu = FindSpan(uk, degU, nU, u), iv = FindSpan(vk, degV, nV, v);
            var nu = Basis(uk, degU, iu, u);
            var nv = Basis(vk, degV, iv, v);
            Pt3 p = new(0, 0, 0);
            for (int a = 0; a <= degU; a++)
                for (int b = 0; b <= degV; b++)
                {
                    var c = cps[(iu - degU + a) * nV + iv - degV + b];
                    p += (nu[a] * nv[b]) * new Pt3(c[0], c[1], c[2]);
                }
            return p;
        };
    }
}
