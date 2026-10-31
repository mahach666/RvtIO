namespace RvtIO.Geometry.Meshing;

internal sealed class FreeformSolid
{
    private sealed class Edge
    {
        public int Marker;
        public FfCurve Curve = null!;
        public double Lo, Hi;
        public double[] BaseTs = null!;
        public double[] Ts = null!;
        public readonly List<Use> Uses = new();
    }

    private sealed class Use
    {
        public Edge Edge = null!;
        public Face Face = null!;
        public MeshCoedge Coedge = null!;
        public FfParamCurve PCurve = null!;
        public bool Projected;
    }

    private sealed class Face
    {
        public BrFace Src = null!;
        public Func<double, double, Pt3> S = null!;
        public double[] Env = null!; // u0, u1, v0, v1
        public List<List<Use>> Loops = new();
        public FfNode Tree = null!;
    }

    private const double WLo = 0.3102637, WHi = 0.6897363000000001;
    private const double CLo = 0.3102637, CHi = 0.6897363;

    private readonly double surfTol, maxEdge;
    private readonly List<Edge> edges = new();
    private readonly Dictionary<int, Edge> edgeByMarker = new();
    private readonly List<Face> faces = new();

    private FreeformSolid(double lod, double extentsDiagonal)
    {
        surfTol = Tolerances.SurfaceTolerance(lod);
        maxEdge = Tolerances.MaxEdgeLength(extentsDiagonal, lod);
    }

    internal static bool IsFreeform(BrFace f) => f.SurfaceKind is SurfaceKind.Ruled or SurfaceKind.NurbsSurface;

    /// <summary>Diagonal of the bounding box of the B-rep edge end points (MeshCoedge.V1 and V2) of the faces.</summary>
    internal static double BrepDiagonal(IEnumerable<BrFace> faces)
    {
        double[] mn = { double.MaxValue, double.MaxValue, double.MaxValue }, mx = { double.MinValue, double.MinValue, double.MinValue };
        foreach (var f in faces)
            foreach (var l in f.Loops)
                foreach (var c in l.Coedges)
                    foreach (var p in new[] { c.V1, c.V2 })
                        if (p != null)
                            for (int i = 0; i < 3; i++) { mn[i] = Math.Min(mn[i], p[i]); mx[i] = Math.Max(mx[i], p[i]); }
        return mn[0] > mx[0] ? 0.0 : Tolerances.ExtentsDiagonal(mn, mx);
    }

    /// <summary>
    /// Diagonal of the bounding box of the B-rep edge curves (every edge once, over the parameter range the edge sampler uses:
    /// end points, the axis extremes of arcs and ellipses, dense samples of NURBS curves) of the faces.
    /// </summary>
    internal static double CurveDiagonal(IEnumerable<BrFace> faces)
    {
        double[] mn = { double.MaxValue, double.MaxValue, double.MaxValue }, mx = { double.MinValue, double.MinValue, double.MinValue };
        var seen = new HashSet<int>();
        foreach (var f in faces)
            foreach (var l in f.Loops)
                foreach (var c in l.Coedges)
                {
                    if (c.EdgeMarker < 0 || !seen.Add(c.EdgeMarker)) continue;
                    var curve = FfCurve.Create(c.Curve);
                    if (curve == null) continue;
                    var (lo, hi) = EdgeRange(c, curve);
                    curve.GrowBox(lo, hi, mn, mx);
                }
        return mn[0] > mx[0] ? 0.0 : Tolerances.ExtentsDiagonal(mn, mx);
    }

    /// <summary>Meshes one freeform face with the freeform faces of face.Solid (or the face alone) as its neighbours.</summary>
    internal static bool TryMeshFace(BrFace face, double lod, double extentsDiagonal, out Mesh mesh)
    {
        mesh = null!;
        var solid = face.Solid ?? new[] { face };
        if (!solid.Contains(face)) solid = solid.Append(face).ToList();
        var s = Create(solid, lod, extentsDiagonal);
        return s != null && s.TryMesh(face, out mesh);
    }

    /// <summary>Prepares the solid (edge samples and grids) for one level of detail; null when any freeform face of the solid has data
    /// the rules do not cover.</summary>
    internal static FreeformSolid? Create(IReadOnlyList<BrFace> solid, double lod, double extentsDiagonal)
    {
        if (!double.IsFinite(lod) || lod <= 0.0 || !double.IsFinite(extentsDiagonal) || extentsDiagonal <= 0.0) return null;
        var s = new FreeformSolid(lod, extentsDiagonal);
        try
        {
            return s.Build(solid) ? s : null;
        }
        catch (Exception ex) when (IsRuleFailure(ex))
        {
            return null;
        }
    }

    internal static bool IsRuleFailure(Exception ex) =>
        ex is IndexOutOfRangeException or ArgumentOutOfRangeException or KeyNotFoundException or InvalidOperationException
            or NullReferenceException or ArgumentException or DivideByZeroException or OverflowException;

    private bool Build(IReadOnlyList<BrFace> solid)
    {
        double dev = 0.5 * surfTol;
        foreach (var src in solid)
        {
            bool ff = IsFreeform(src);
            Face? face = null;
            if (ff)
            {
                if (!SurfaceOf(src, out var sf, out var env) || src.Loops.Count == 0) return false;
                face = new Face { Src = src, S = sf!, Env = env! };
                faces.Add(face);
            }
            foreach (var loop in src.Loops)
            {
                if (ff && loop.Coedges.Count == 0) return false;
                var uses = new List<Use>();
                foreach (var c in loop.Coedges)
                {
                    var curve = FfCurve.Create(c.Curve);
                    if (ff && (c.EdgeMarker < 0 || curve == null)) return false;
                    if (curve == null || c.EdgeMarker < 0) continue;
                    if (!edgeByMarker.TryGetValue(c.EdgeMarker, out var e))
                    {
                        var (lo, hi) = EdgeRange(c, curve);
                        e = new Edge { Marker = c.EdgeMarker, Curve = curve, Lo = lo, Hi = hi };
                        e.BaseTs = BaseParams(curve, lo, hi, dev);
                        edgeByMarker[c.EdgeMarker] = e;
                        edges.Add(e);
                    }
                    if (!ff) continue;
                    var pc = FfParamCurve.Create(c.ParamCurve, curve.IsPeriodic);
                    if (pc == null) return false;
                    var use = new Use { Edge = e, Face = face!, Coedge = c, PCurve = pc, Projected = c.ParamCurve!.Projected };
                    e.Uses.Add(use);
                    uses.Add(use);
                }
                face?.Loops.Add(uses);
            }
        }
        foreach (var f in faces)
        {
            double uLo = double.MaxValue, uHi = double.MinValue, vLo = double.MaxValue, vHi = double.MinValue;
            foreach (var loop in f.Loops)
                foreach (var use in loop)
                {
                    var e = use.Edge;
                    var ts = e.BaseTs;
                    if (ts.Length > 10)
                    {
                        ts = new double[10];
                        for (int k = 0; k < 10; k++) ts[k] = e.Lo + k * (e.Hi - e.Lo) / 9.0;
                    }
                    foreach (double t in ts)
                    {
                        var (u, v) = UvOf(use, t);
                        uLo = Math.Min(uLo, u); uHi = Math.Max(uHi, u); vLo = Math.Min(vLo, v); vHi = Math.Max(vHi, v);
                    }
                }
            if (!(uLo <= uHi) || !(vLo <= vHi)) return false;
            f.Tree = FreeformGrid.BuildTree(f.S, uLo, uHi, vLo, vHi, surfTol, maxEdge);
        }
        foreach (var e in edges)
        {
            var adjacent = e.Uses.Where(u => u.Face.Tree.Lo != null).ToList();
            var ts = RefineByGrids(e.Curve, e.BaseTs, surfTol, adjacent);
            // the halving of long pieces must terminate: finite points and no more than 2^17 pieces per interval
            var pts = ts.Select(e.Curve.At).ToArray();
            for (int i = 0; i < pts.Length; i++)
                if (!pts[i].IsFinite || (i > 0 && (pts[i] - pts[i - 1]).Norm > maxEdge * 131072.0)) return false;
            e.Ts = ArcSampler.BisectLong(ts, t => e.Curve.At(t).ToArray(), maxEdge).ToArray();
        }
        return true;
    }

    /// <summary>The surface evaluator and its parameter envelope (u0, u1, v0, v1).</summary>
    private static bool SurfaceOf(BrFace f, out Func<double, double, Pt3>? s, out double[]? env)
    {
        s = null; env = null;
        double[]? e = f.Envelope is { Length: 4 } q && q.All(double.IsFinite) && q[0] <= q[2] && q[1] <= q[3] ? new[] { q[0], q[2], q[1], q[3] } : null;
        if (f.SurfaceKind == SurfaceKind.Ruled && f.Ruled is { } r)
        {
            s = e == null ? null : FfSurfaces.Ruled(r);
            env = e;
        }
        else if (f.SurfaceKind == SurfaceKind.NurbsSurface && f.Nurbs is { } n)
        {
            s = FfSurfaces.Nurbs(n);
            if (s != null)
            {
                int nU = n.KnotsU.Length - n.DegreeU - 1, nV = n.KnotsV.Length - n.DegreeV - 1;
                env = e ?? new[] { n.KnotsU[n.DegreeU], n.KnotsU[nU], n.KnotsV[n.DegreeV], n.KnotsV[nV] };
            }
        }
        return s != null;
    }

    private static (double Lo, double Hi) NormalizeRange(double a, double b) => ArcSampler.NormalizeRange(a, b);

    /// <summary>The parameter range handed to the edge sampler: paramOf of the two edge vertices for arcs and ellipses (normalised
    /// into [0, 2 pi]), the curve interval for everything else or when a vertex is unknown.</summary>
    private static (double Lo, double Hi) EdgeRange(MeshCoedge c, FfCurve curve)
    {
        var src = c.Curve!;
        if (c.V1 == null || c.V2 == null) return (src.Lo, src.Hi);
        if (curve.Kind == CurveKind.Arc) return NormalizeRange(ArcSampler.ParamOf(src.Arc!, c.V1), ArcSampler.ParamOf(src.Arc!, c.V2));
        if (curve.Kind == CurveKind.Ellipse) return NormalizeRange(EllipseParamOf(src.Ellipse!, c.V1), EllipseParamOf(src.Ellipse!, c.V2));
        return (src.Lo, src.Hi);
    }

    private static double EllipseParamOf(Ellipse e, double[] p)
    {
        double dx = p[0] - e.Center[0], dy = p[1] - e.Center[1], dz = p[2] - e.Center[2];
        double a = dy * e.MajorAxis[1] + dx * e.MajorAxis[0] + dz * e.MajorAxis[2];
        double b = dy * e.MinorAxis[1] + dx * e.MinorAxis[0] + dz * e.MinorAxis[2];
        return ArcSampler.WrapPeriod(e.Start, e.End, Math.Atan2(b / e.MinorRadius, a / e.MajorRadius));
    }

    private static bool IsStraight(FfCurve c, double lo, double hi)
    {
        var p0 = c.At(lo); var p1 = c.At(hi);
        if ((c.At((lo + hi) * 0.5) - (p0 + p1) * 0.5).Norm > 1e-10) return false;
        foreach (var (wa, wb) in new[] { (WLo, WHi), (WHi, WLo) })
            if ((c.At(lo * wa + hi * wb) - (p0 * wa + p1 * wb)).Norm > 1e-10) return false;
        return true;
    }

    private static List<double> TessCurve(FfCurve c, double t0, double t1, double eps)
    {
        var ts = new List<double> { t0 };
        void Rec(double a, double b, int depth)
        {
            if (depth > 20) return;
            var pa = c.At(a); var pb = c.At(b);
            double m = (a + b) * 0.5;
            var pm = c.At(m);
            bool ok = (pm - (pa + pb) * 0.5).Norm <= eps;
            if (ok) ok = (c.At(a * CLo + b * CHi) - (pb * CHi + pa * CLo)).Norm <= eps;
            if (ok) ok = (c.At(a * CHi + b * CLo) - (pa * CHi + pb * CLo)).Norm <= eps;
            if (!ok)
            {
                Rec(a, m, depth + 1);
                ts.Add(m);
                Rec(m, b, depth + 1);
            }
        }
        Rec(t0, t1, 0);
        ts.Add(t1);
        return ts;
    }

    private static double ArcStep(double dev, double radius)
    {
        double x = dev / radius;
        if (x > 0.0)
        {
            if (x >= 0.000005) return 1.0 - x > -1.0 ? Math.Min(0.7853981633974483, 2.0 * Math.Acos(1.0 - x)) : 1.570796326794897;
            return 0.006283185307179587;
        }
        return 0.1963495408493621;
    }

    private static List<double> EqualParams(double lo, double hi, int n)
    {
        double d = (hi - lo) / n;
        var ts = new List<double>(n + 1);
        for (int k = 0; k <= n; k++) ts.Add(lo + k * d);
        return ts;
    }

    /// <summary>Samples of an edge before the grid refinement: straight edges keep their end points, an arc is divided into equal
    /// steps, an ellipse likewise, a NURBS curve by chord recursion; a curved edge has at least 4 samples (6 when closed).</summary>
    private static double[] BaseParams(FfCurve c, double lo, double hi, double dev)
    {
        bool closed = c.IsClosed(lo, hi);
        if (!closed && IsStraight(c, lo, hi)) return new[] { lo, hi };
        List<double> ts;
        switch (c.Kind)
        {
            case CurveKind.Arc:
            {
                if (c.Radius == 0.0) { ts = new List<double> { lo }; break; }
                int n = (int)Math.Ceiling(Math.Abs(hi - lo) / ArcStep(dev, c.Radius));
                ts = n == 0 ? new List<double> { lo } : EqualParams(lo, hi, n);
                break;
            }
            case CurveKind.Ellipse:
            {
                double r = Math.Max(c.Minor, c.Major), d = dev;
                if (d < 0.0 || d > r + r) d = r * 0.5;
                int n;
                if (Math.Abs(r) <= 1e-10) n = 32;
                else
                {
                    double x = d / r, step;
                    if (x <= 0.0) step = 0.1963495408493621;
                    else if (x < 0.000005) step = 0.006283185307179587;
                    else if (1.0 - x <= -1.0) step = 1.570796326794897;
                    else step = Math.Min(0.7853981633974483, 2.0 * Math.Acos(1.0 - x));
                    n = step <= 1e-10 ? 32 : Math.Min((int)(Math.Abs(hi - lo) / step) + 1, 1500);
                }
                ts = EqualParams(lo, hi, n);
                break;
            }
            case CurveKind.Nurbs:
                ts = TessCurve(c, lo, hi, dev);
                break;
            case CurveKind.Line:
                ts = new List<double> { lo, hi };
                break;
            default:
                throw new InvalidOperationException("edge curve kind");
        }
        int need = (closed ? 2 : 0) + 4;
        if (ts.Count < need)
        {
            ts = new List<double>(need);
            for (int i = 0; i < need; i++) ts.Add(lo + i * (hi - lo) / (need - 1));
        }
        return ts.ToArray();
    }

    /// <summary>The uv of an edge sample on the face of the use: the parameter curve, or the closest surface point for a projected one.</summary>
    private static (double U, double V) UvOf(Use u, double t) =>
        u.Projected ? ProjectUv(u.Face.S, u.Edge.Curve.At(t), u.PCurve.At(t), u.Face.Env) : u.PCurve.At(t);

    private List<double> RefineByGrids(FfCurve c, double[] ts, double thr, List<Use> adjacent)
    {
        if (adjacent.Count == 0) return ts.ToList();
        var pts = ts.Select(c.At).ToArray();
        var res = new List<double> { ts[0] };
        for (int i = 0; i + 1 < ts.Length; i++)
        {
            double a = ts[i], b = ts[i + 1];
            int n = 0;
            var d = pts[i] - pts[i + 1];
            if (thr <= 0.0 || thr * thr <= d.Dot(d))
                foreach (var u in adjacent) n = Math.Max(n, FreeformGrid.CountCross(u.Face.Tree, UvOf(u, a), UvOf(u, b)));
            if (n > 2)
                for (int k = 1; k < n; k++) res.Add(a + k * (b - a) / n);
            res.Add(b);
        }
        return res;
    }

    /// <summary>The final 3D samples of an edge in the direction of its curve (shared by every face the edge bounds), null for an
    /// unknown edge marker.</summary>
    /// <summary>The samples of an edge that bounds a free form face of the solid (the planar neighbours use them), null for any other edge.</summary>
    internal double[][]? SharedEdgeSamples(int marker) =>
        edgeByMarker.TryGetValue(marker, out var e) && e.Uses.Count > 0 ? e.Ts.Select(t => e.Curve.At(t).ToArray()).ToArray() : null;

    internal double[][]? EdgeSamples(int marker) =>
        edgeByMarker.TryGetValue(marker, out var e) ? e.Ts.Select(t => e.Curve.At(t).ToArray()).ToArray() : null;

    /// <summary>
    /// The walk of a grid cell can leave a ring of two points beside the polygon of the cell: the sliver of the loop that runs
    /// along the cell border.
    /// </summary>
    private static readonly bool DropLineRings = Environment.GetEnvironmentVariable("RVTIO_X_LINERING") != "0";

    /// <summary>Why the last freeform face was refused (diagnostics only).</summary>
    [ThreadStatic] internal static string? LastRefusal;

    /// <summary>Meshes one freeform face of the solid; false when the face is not part of the solid or a rule does not cover it.</summary>
    internal bool TryMesh(BrFace src, out Mesh mesh)
    {
        mesh = null!;
        LastRefusal = null;
        var face = faces.FirstOrDefault(f => f.Src == src);
        if (face == null) { LastRefusal = "face not in the solid"; return false; }
        try
        {
            var m = MeshFace(face);
            if (m == null) { LastRefusal ??= "ring triangulation failed"; return false; }
            mesh = m;
            return true;
        }
        catch (Exception ex) when (IsRuleFailure(ex))
        {
            LastRefusal = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private bool PcurveValid(Face f, Use use)
    {
        var e = use.Edge;
        double ta = e.Lo + 0.2 * (e.Hi - e.Lo), tb = e.Hi - 0.2 * (e.Hi - e.Lo);
        var p1 = e.Curve.At(ta); var p2 = e.Curve.At(tb);
        var (ua, va) = use.PCurve.At(ta); var (ub, vb) = use.PCurve.At(tb);
        var q1 = f.S(ua, va); var q2 = f.S(ub, vb);
        double chord = (p1 - p2).Norm / 10.0;
        double tol = Math.Abs(chord) > 1e-10 ? Math.Min(0.5, chord) : 0.5;
        double d11 = (p1 - q1).Norm, d12 = (p1 - q2).Norm, d22 = (p2 - q2).Norm, d21 = (p2 - q1).Norm;
        return d12 > d11 && d21 > d22 && d11 <= tol && d22 <= tol;
    }

    /// <summary>
    /// Closest point of the surface to p by damped Gauss-Newton from the parameter curve value (the mesher's paramOf for an
    /// edge whose parameter curve it distrusts).
    /// </summary>
    private static (double U, double V) ProjectUv(Func<double, double, Pt3> s, Pt3 p, (double U, double V) uv0, double[] env)
    {
        double u = uv0.U, v = uv0.V;
        const double h = 1e-6, tol = 1e-14;
        for (int it = 0; it < 60; it++)
        {
            var r = s(u, v) - p;
            var su = (s(u + h, v) - s(u - h, v)) / (2 * h);
            var sv = (s(u, v + h) - s(u, v - h)) / (2 * h);
            double a = su.Dot(su), b = su.Dot(sv), c = sv.Dot(sv);
            double g0 = -su.Dot(r), g1 = -sv.Dot(r);
            // minimum norm least squares step of J d = -r through the eigen decomposition of J^T J
            double mid = (a + c) * 0.5, rad = Math.Sqrt((a - c) * 0.5 * ((a - c) * 0.5) + b * b);
            double l1 = mid + rad, l2 = mid - rad, th = 0.5 * Math.Atan2(2 * b, a - c);
            double[][] vec = { new[] { Math.Cos(th), Math.Sin(th) }, new[] { -Math.Sin(th), Math.Cos(th) } };
            double cut = 3 * 2.220446049250313e-16;
            double d0 = 0, d1 = 0;
            for (int k = 0; k < 2; k++)
            {
                double l = k == 0 ? l1 : l2;
                if (!(l > cut * cut * l1)) continue;
                double coef = (vec[k][0] * g0 + vec[k][1] * g1) / l;
                d0 += coef * vec[k][0]; d1 += coef * vec[k][1];
            }
            double step = 1.0, n0 = r.Dot(r);
            for (int k = 0; k < 20; k++)
            {
                var rr = s(u + step * d0, v + step * d1) - p;
                if (rr.Dot(rr) <= n0 * (1 + 1e-12)) break;
                step *= 0.5;
            }
            u += step * d0; v += step * d1;
            u = Math.Min(Math.Max(u, env[0]), env[1]); v = Math.Min(Math.Max(v, env[2]), env[3]);
            if (Math.Abs(step * d0) < tol && Math.Abs(step * d1) < tol) break;
        }
        return (u, v);
    }

    private List<List<FfVtx>> BuildLoops(Face f)
    {
        var loops = new List<List<FfVtx>>();
        foreach (var loop in f.Loops)
        {
            var pts = new List<FfVtx>();
            foreach (var use in loop)
            {
                var e = use.Edge;
                bool usePc = !use.Projected && PcurveValid(f, use);
                var one = new List<FfVtx>(e.Ts.Length);
                foreach (double t in e.Ts)
                {
                    var p3 = e.Curve.At(t);
                    var uv = use.PCurve.At(t);
                    if (!usePc) uv = ProjectUv(f.S, p3, uv, f.Env);
                    one.Add(new FfVtx(uv.U, uv.V, p3));
                }
                if (!use.Coedge.OrientToLoop) one.Reverse();
                pts.AddRange(one);
            }
            var o = new List<FfVtx>();
            foreach (var v in pts)
                if (o.Count == 0 || !FreeformGrid.Eq(o[^1].U, o[^1].V, v.U, v.V)) o.Add(v);
            if (o.Count > 1 && FreeformGrid.Eq(o[0].U, o[0].V, o[^1].U, o[^1].V)) o.RemoveAt(o.Count - 1);
            if (o.Count == 0) throw new InvalidOperationException("empty loop");
            if (!f.Src.OrientToSurface)
            {
                var r = new List<FfVtx> { o[0] };
                for (int i = o.Count - 1; i >= 1; i--) r.Add(o[i]);
                o = r;
            }
            loops.Add(o);
        }
        return loops;
    }

    private static double RingArea(List<FfVtx> ring)
    {
        double a = 0.0;
        int n = ring.Count;
        for (int k = 0; k < n; k++)
        {
            var p = ring[k]; var q = ring[(k + 1) % n];
            a += p.U * q.V - q.U * p.V;
        }
        return 0.5 * a;
    }

    private static int Winding(List<FfVtx> loop, double pu, double pv)
    {
        int wn = 0, n = loop.Count;
        for (int k = 0; k < n; k++)
        {
            var a = loop[k]; var b = loop[(k + 1) % n];
            if (a.V <= pv)
            {
                if (b.V > pv && (b.U - a.U) * (pv - a.V) - (pu - a.U) * (b.V - a.V) > 0) wn++;
            }
            else if (b.V <= pv && (b.U - a.U) * (pv - a.V) - (pu - a.U) * (b.V - a.V) < 0) wn--;
        }
        return wn;
    }

    private static List<(int A, int B, int C)> TriangulateCounts((double X, double Y)[] pts, List<int> prof, out bool ok)
    {
        var tris = new List<(int, int, int)>();
        ok = true;
        int pos = 0;
        while (pos < prof.Count)
        {
            int n = prof[pos];
            if (n <= 0) { pos++; continue; }
            var region = new List<int[]> { prof.GetRange(pos + 1, n).ToArray() };
            pos += 1 + n;
            while (pos < prof.Count && prof[pos] < 0)
            {
                int m = -prof[pos];
                region.Add(prof.GetRange(pos + 1, m).ToArray());
                pos += 1 + m;
            }
            foreach (int i in region.SelectMany(r => r))
                if (i < 0 || i >= pts.Length) throw new IndexOutOfRangeException("profile index");
            var pieces = GeTess.ConvexPiecesStrict(pts, region, out bool regionOk);
            if (!regionOk) ok = false;
            foreach (var piece in pieces)
                for (int j = 1; j < piece.Length - 1; j++) tris.Add((piece[0], piece[j], piece[j + 1]));
        }
        return tris;
    }

    /// <summary>
    /// The rings of one cell (or the loops of an undivided face) go to the profile triangulation in one call. reverse: the
    /// rings are clockwise grid cell polygons and are reversed first; the sign of the area then decides outer or hole, and the
    /// first ring is made an outer one.
    /// </summary>
    internal static bool EmitRings(List<(FfVtx, FfVtx, FfVtx)> tris, List<List<FfVtx>> ringList, bool flip, bool reverse,
        Action<List<FfVtx>>? tag)
    {
        double sign = reverse ? 1.0 : -1.0;
        var rings = new List<List<FfVtx>>(ringList);
        // only when a real ring is left: a cell of nothing but short rings stays a refusal (it is not a mesh without triangles)
        if (DropLineRings && rings.Count > 1 && rings.Any(r => r.Count >= 3)) rings.RemoveAll(r => r.Count < 3);
        var areas = rings.Select(r => sign * RingArea(r)).ToList();
        if (rings.Count > 1 && areas[0] >= 0.0)
            for (int i = 1; i < rings.Count; i++)
                if (areas[i] < 0.0)
                {
                    (rings[0], rings[i]) = (rings[i], rings[0]);
                    (areas[0], areas[i]) = (areas[i], areas[0]);
                    break;
                }
        if (reverse) rings = rings.Select(r => { var c = new List<FfVtx>(r); c.Reverse(); return c; }).ToList();
        if (tag != null) foreach (var r in rings) tag(r);
        var flat = new List<FfVtx>();
        var prof = new List<int>();
        for (int k = 0; k < rings.Count; k++)
        {
            int n = rings[k].Count, b = flat.Count;
            prof.Add(areas[k] > 0.0 ? -n : n);
            for (int i = 0; i < n; i++) { flat.Add(rings[k][i]); prof.Add(b + i); }
        }
        var tri = TriangulateCounts(flat.Select(v => (v.U, v.V)).ToArray(), prof, out bool ok);
        if (!ok && rings.Count > 1)
        {
            // the failure fallback of the mesher is not reproduced
            LastRefusal = $"ring triangulation failed: {rings.Count} rings of {string.Join("/", rings.Select(r => r.Count))} points, areas {string.Join("/", areas.Select(a => a.ToString("G6")))}";
            if (Environment.GetEnvironmentVariable("RVTIO_WHY") == "1")
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                for (int k = 0; k < rings.Count; k++)
                    DiagnosticSink.WriteLine($"[meshwhy] ring {k} area {areas[k].ToString("R", ic)}: " + string.Join(" ", rings[k].Select(v => $"({v.U.ToString("R", ic)},{v.V.ToString("R", ic)})")));
            }
            return false;
        }
        foreach (var (a, b, c) in tri) tris.Add(flip ? (flat[a], flat[c], flat[b]) : (flat[a], flat[b], flat[c]));
        return true;
    }

    /// <summary>Grid points of the cell that lie inside a ring edge are inserted into it, ordered along the edge.</summary>
    private static List<FfVtx> InsertTpoints(List<FfVtx> ring, List<(double U, double V)> tps)
    {
        const double tol = 1e-10;
        if (tps.Count == 0) return ring;
        var o = new List<FfVtx>();
        int n = ring.Count;
        for (int k = 0; k < n; k++)
        {
            var a = ring[k]; var b = ring[(k + 1) % n];
            o.Add(a);
            double dx = b.U - a.U, dy = b.V - a.V, l2 = dx * dx + dy * dy;
            if (l2 == 0.0) continue;
            var hits = new List<(double T, int J)>();
            for (int j = 0; j < tps.Count; j++)
            {
                var p = tps[j];
                double t = ((p.U - a.U) * dx + (p.V - a.V) * dy) / l2;
                if (t < -tol || t > 1 + tol) continue;
                double ex = p.U - (a.U + t * dx), ey = p.V - (a.V + t * dy);
                if (Math.Sqrt(ex * ex + ey * ey) > tol) continue;
                if (Math.Abs(t) <= tol || Math.Abs(t - 1) <= tol) continue;
                hits.Add((t, j));
            }
            hits.Sort((x, y) => x.T != y.T ? x.T.CompareTo(y.T) : x.J.CompareTo(y.J));
            foreach (var h in hits) o.Add(new FfVtx(tps[h.J].U, tps[h.J].V));
        }
        return o;
    }

    private Mesh? MeshFace(Face f)
    {
        var loops = BuildLoops(f);
        double uLo = loops.Min(l => l.Min(v => v.U)), uHi = loops.Max(l => l.Max(v => v.U));
        double vLo = loops.Min(l => l.Min(v => v.V)), vHi = loops.Max(l => l.Max(v => v.V));
        var root = FreeformGrid.BuildTree(f.S, uLo, uHi, vLo, vHi, surfTol, maxEdge);
        bool flip = f.Src.OrientToSurface;
        var tris = new List<(FfVtx, FfVtx, FfVtx)>();
        if (root.Lo == null)
        {
            // the ring vertices lie exactly on the rectangle borders; parameter curve noise of 1e-14 is snapped away (it decides ties)
            foreach (var lp in loops)
                foreach (var v in lp)
                {
                    v.U = Math.Abs(v.U - root.U0) <= FreeformGrid.Tol ? root.U0 : Math.Abs(v.U - root.U1) <= FreeformGrid.Tol ? root.U1 : v.U;
                    v.V = Math.Abs(v.V - root.V0) <= FreeformGrid.Tol ? root.V0 : Math.Abs(v.V - root.V1) <= FreeformGrid.Tol ? root.V1 : v.V;
                }
            if (!EmitRings(tris, loops, flip, false, null)) return null;
            return ToMesh(tris, f.S);
        }
        foreach (var lp in loops)
            for (int k = 0; k < lp.Count; k++)
                FreeformGrid.SegToCells(root, lp[(k + 1) % lp.Count], lp[k], 0, f.S);
        FreeformGrid.AddTpoints(root);
        var (nodes, poolIds) = FreeformGrid.AssignPool(root);
        int counter = 0;
        void Tag(List<FfVtx> vs)
        {
            foreach (var v in vs)
            {
                if (v.Pid >= 0 || v.Seq >= 0) continue;
                var k = FreeformGrid.Key(v.U, v.V);
                if (v.F17 == 0 && poolIds.TryGetValue(k, out int pid)) v.Pid = pid;
                else
                {
                    v.Seq = counter++;
                    if (poolIds.TryGetValue(k, out int hint)) v.PoolHint = hint;
                }
            }
        }
        foreach (var c in nodes.Where(n => n.Lo == null))
        {
            var rings = c.Recs.Count > 0 ? FreeformGrid.WalkCell(c) : new List<List<FfVtx>>();
            if (rings.Count == 0)
            {
                double ctrU = (c.U0 + c.U1) / 2, ctrV = (c.V0 + c.V1) / 2;
                if (loops.Sum(lp => Winding(lp, ctrU, ctrV)) == 0) continue;
                FfVtx bl = new(c.U0, c.V0), tl = new(c.U0, c.V1), tr = new(c.U1, c.V1), br = new(c.U1, c.V0);
                Tag(new List<FfVtx> { bl, tl, tr, br });
                if (c.Tp.Count == 0)
                {
                    if (flip) { tris.Add((bl, tr, tl)); tris.Add((bl, br, tr)); }
                    else { tris.Add((bl, tl, tr)); tris.Add((bl, tr, br)); }
                    continue;
                }
                rings = new List<List<FfVtx>> { new() { bl, tl, tr, br } };
            }
            if (!EmitRings(tris, rings.Select(r => InsertTpoints(r, c.Tp)).ToList(), flip, true, Tag)) return null;
        }
        if (f.Src.SurfaceKind != SurfaceKind.Ruled) tris = FreeformSeams.RemoveBoundaryVertices(tris, f.S, f.Env, flip);
        return ToMesh(tris, f.S);
    }

    private static Mesh ToMesh(List<(FfVtx A, FfVtx B, FfVtx C)> tris, Func<double, double, Pt3> s)
    {
        var idx = new Dictionary<(long, long, long), int>();
        var verts = new List<double[]>();
        int Id(FfVtx v)
        {
            var p = v.P3 ?? s(v.U, v.V);
            var key = ((long)Math.Round(p.X * 1e9), (long)Math.Round(p.Y * 1e9), (long)Math.Round(p.Z * 1e9));
            if (!idx.TryGetValue(key, out int i)) { i = verts.Count; idx[key] = i; verts.Add(p.ToArray()); }
            return i;
        }
        var t = new List<int[]>();
        foreach (var (a, b, c) in tris)
        {
            int ia = Id(a), ib = Id(b), ic = Id(c);
            if (ia != ib && ib != ic && ia != ic) t.Add(new[] { ia, ib, ic }); // triangles collapsed in 3D (apex of a point profile) are dropped
        }
        return new Mesh { Vertices = verts.ToArray(), Triangles = t.ToArray() };
    }
}
