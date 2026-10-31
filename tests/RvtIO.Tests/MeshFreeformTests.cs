using System.Diagnostics;
using System.Text.Json;
using RvtIO.Geometry.Meshing;
using Xunit;
using Xunit.Abstractions;

namespace RvtIO.Tests;

/// <summary>
/// Freeform faces (kRuled, kNurbSurface) against the expected meshes (fixtures/FaceMesh/freeform.mj.json.gz: BREP edges with
/// parameter curves, surfaces, expected meshes at the four LODs and the baseline verdict per face and LOD). "Exact" = every vertex
/// within 1e-9 ft of an expected vertex and the same set of oriented triangles; triangles collapsed in 3D are dropped on both sides
/// (the apex of a point profile). The extents diagonal D is the diagonal of the bounding box of the B-rep edge end points of the geometry,
/// NOT of the expected mesh.
/// </summary>
public class MeshFreeformTests
{
    private readonly ITestOutputHelper output;
    public MeshFreeformTests(ITestOutputHelper output) => this.output = output;

    private static readonly double[] Lods = { 0.1, 0.2, 0.3, 0.4 };

    // Faces the baseline gets exact and this port does not ("solidId:marker@lod"). Empty: the baseline pass set is reproduced.
    private static readonly HashSet<string> KnownNotExact = new();


    internal sealed class FfFace
    {
        public BrFace Face = null!;
        public int Marker;
        public bool Target;
        public Dictionary<double, (double[][] V, int[][] T)> Reference = new();
        public Dictionary<double, bool> PythonBrep = new(), PythonCurve = new();
        public JsonElement Raw;
    }

    internal sealed class FfSolid
    {
        public string Id = "";
        /// <summary>Extents diagonal: of the B-rep edge end points (D) and of the B-rep edge curves (Dc) of the whole geometry.</summary>
        public double D, Dc;
        public List<FfFace> Faces = new();
        public Dictionary<int, MeshCurve> Curves = new();
        public Dictionary<int, (double[]? A, double[]? B)> Vertices = new();
    }

    private static double[] Arr(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();
    private static double[][] Arr2(JsonElement e) => e.EnumerateArray().Select(Arr).ToArray();

    private static MeshCurve? CurveOf(JsonElement c)
    {
        if (c.ValueKind == JsonValueKind.Null) return null;
        double lo = c.GetProperty("lo").GetDouble(), hi = c.GetProperty("hi").GetDouble();
        switch (c.GetProperty("k").GetString())
        {
            case "L":
                return new MeshCurve { Kind = CurveKind.Line, Lo = lo, Hi = hi, P0 = Arr(c.GetProperty("a")), P1 = Arr(c.GetProperty("b")) };
            case "A":
                return new MeshCurve
                {
                    Kind = CurveKind.Arc, Lo = lo, Hi = hi,
                    Arc = new Arc
                    {
                        Center = Arr(c.GetProperty("c")), Radius = c.GetProperty("r").GetDouble(), Normal = Arr(c.GetProperty("n")),
                        Ref = Arr(c.GetProperty("x")), Start = c.GetProperty("s").GetDouble(), End = c.GetProperty("e").GetDouble(),
                    },
                };
            case "E":
                return new MeshCurve
                {
                    Kind = CurveKind.Ellipse, Lo = lo, Hi = hi,
                    Ellipse = new Ellipse
                    {
                        Center = Arr(c.GetProperty("c")), MajorAxis = Arr(c.GetProperty("u")), MinorAxis = Arr(c.GetProperty("w")),
                        MajorRadius = c.GetProperty("ra").GetDouble(), MinorRadius = c.GetProperty("rb").GetDouble(),
                        Start = c.GetProperty("s").GetDouble(), End = c.GetProperty("e").GetDouble(),
                    },
                };
            default:
                return new MeshCurve
                {
                    Kind = CurveKind.Nurbs, Lo = lo, Hi = hi,
                    Nurbs = new Nurbs
                    {
                        Degree = c.GetProperty("d").GetInt32(), Knots = Arr(c.GetProperty("kn")), ControlPoints = Arr2(c.GetProperty("p")),
                        Weights = c.TryGetProperty("w", out var w) ? Arr(w) : null,
                    },
                };
        }
    }

    internal static List<FfSolid> Load(string name = "freeform.mj.json.gz")
    {
        using var doc = MeshFixtures.Load(name);
        var list = new List<FfSolid>();
        foreach (var s in doc.RootElement.GetProperty("solids").EnumerateArray())
        {
            var solid = new FfSolid { Id = s.GetProperty("id").GetString()!, D = s.GetProperty("D").GetDouble(), Dc = s.GetProperty("Dc").GetDouble() };
            foreach (var e in s.GetProperty("edges").EnumerateObject())
            {
                int marker = int.Parse(e.Name);
                solid.Curves[marker] = CurveOf(e.Value.GetProperty("c"))!;
                solid.Vertices[marker] = e.Value.TryGetProperty("pa", out var pa) ? (Arr(pa), Arr(e.Value.GetProperty("pb"))) : (null, null);
            }
            foreach (var f in s.GetProperty("faces").EnumerateArray())
            {
                var ff = new FfFace { Marker = f.GetProperty("m").GetInt32(), Target = f.TryGetProperty("t", out _), Raw = f.Clone() };
                var loops = new List<Loop>();
                foreach (var l in f.GetProperty("loops").EnumerateArray())
                {
                    var coedges = new List<MeshCoedge>();
                    foreach (var c in l.EnumerateArray())
                    {
                        int em = c.GetProperty("e").GetInt32();
                        var (va, vb) = solid.Vertices[em];
                        coedges.Add(new MeshCoedge
                        {
                            EdgeMarker = em, OrientToLoop = c.GetProperty("ol").GetBoolean(), Curve = solid.Curves[em], V1 = va, V2 = vb,
                            ParamCurve = new ParamCurve
                            {
                                Lo = c.GetProperty("pl").GetDouble(), Hi = c.GetProperty("ph").GetDouble(),
                                Nurbs = new Nurbs { Degree = c.GetProperty("pd").GetInt32(), Knots = Arr(c.GetProperty("pk")), ControlPoints = Arr2(c.GetProperty("pp")) },
                            },
                        });
                    }
                    loops.Add(new Loop { Coedges = coedges });
                }
                string kind = f.GetProperty("kind").GetString()!;
                bool os = f.GetProperty("os").GetBoolean();
                var sf = f.GetProperty("surf");
                if (kind == "kRuled")
                    ff.Face = new BrFace
                    {
                        SurfaceKind = SurfaceKind.Ruled, OrientToSurface = os, Loops = loops, Envelope = Arr(f.GetProperty("env")),
                        Ruled = new RuledSurface
                        {
                            Profile0 = CurveOf(sf.GetProperty("pr")[0]), Profile1 = CurveOf(sf.GetProperty("pr")[1]),
                            Point0 = Arr(sf.GetProperty("p0")), Point1 = Arr(sf.GetProperty("p1")),
                        },
                    };
                else if (kind == "kNurbSurface")
                    ff.Face = new BrFace
                    {
                        SurfaceKind = SurfaceKind.NurbsSurface, OrientToSurface = os, Loops = loops, Envelope = Arr(f.GetProperty("env")),
                        Nurbs = new NurbsSurface
                        {
                            DegreeU = sf.GetProperty("du").GetInt32(), DegreeV = sf.GetProperty("dv").GetInt32(),
                            KnotsU = Arr(sf.GetProperty("ku")), KnotsV = Arr(sf.GetProperty("kv")), ControlPoints = Arr2(sf.GetProperty("p")),
                        },
                    };
                else
                {
                    var pl = f.GetProperty("plane");
                    ff.Face = new BrFace
                    {
                        SurfaceKind = SurfaceKind.Plane, OrientToSurface = os, Loops = loops, Flipped = f.GetProperty("flipped").GetBoolean(),
                        Plane = new PlaneSurface { Origin = Arr(pl.GetProperty("o")), U = Arr(pl.GetProperty("u")), V = Arr(pl.GetProperty("v")) },
                    };
                }
                if (f.TryGetProperty("o", out var o))
                    foreach (var m in o.EnumerateObject())
                        ff.Reference[double.Parse(m.Name, System.Globalization.CultureInfo.InvariantCulture)] =
                            (Arr2(m.Value[0]), m.Value[1].EnumerateArray().Select(t => t.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray());
                foreach (var (key, dict) in new[] { ("pb", ff.PythonBrep), ("pc", ff.PythonCurve) })
                    if (f.TryGetProperty(key, out var py))
                        foreach (var m in py.EnumerateObject())
                            dict[double.Parse(m.Name, System.Globalization.CultureInfo.InvariantCulture)] = m.Value.GetInt32() != 0;
                solid.Faces.Add(ff);
            }
            // every freeform face of the solid sees the others as the neighbours that refine its shared edges
            var freeform = solid.Faces.Where(f => f.Face.SurfaceKind != SurfaceKind.Plane).Select(f => f.Face).ToList();
            foreach (var f in freeform) f.Solid = freeform;
            list.Add(solid);
        }
        return list;
    }


    /// <summary>Vertices of both meshes are clustered (within tol is one vertex), matched by position, and every triangle becomes the
    /// cyclic triple of cluster ids starting at the smallest id (the winding is kept); triangles collapsed after clustering are dropped.</summary>
    internal static bool Exact(Mesh m, double[][] ov, int[][] ot, double tol = 1e-9)
    {
        var (idM, repM) = Cluster(m.Vertices, tol);
        var (idO, repO) = Cluster(ov, tol);
        var oByCell = new Dictionary<(long, long, long), List<int>>();
        double h = 4 * tol;
        (long, long, long) Cell(double[] p) => ((long)Math.Floor(p[0] / h), (long)Math.Floor(p[1] / h), (long)Math.Floor(p[2] / h));
        for (int j = 0; j < repO.Count; j++)
        {
            var c = Cell(repO[j]);
            if (!oByCell.TryGetValue(c, out var l)) oByCell[c] = l = new List<int>();
            l.Add(j);
        }
        var map = new int[repM.Count];
        var used = new HashSet<int>();
        for (int i = 0; i < repM.Count; i++)
        {
            var c = Cell(repM[i]);
            double best = double.MaxValue;
            int bj = -1;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (oByCell.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out var l))
                            foreach (int j in l)
                            {
                                double d = Math.Max(Math.Abs(repO[j][0] - repM[i][0]), Math.Max(Math.Abs(repO[j][1] - repM[i][1]), Math.Abs(repO[j][2] - repM[i][2])));
                                if (d <= tol && d < best) { best = d; bj = j; }
                            }
            if (bj < 0) return false;
            map[i] = bj;
            used.Add(bj);
        }
        if (used.Count != repO.Count) return false;
        var km = Keys(m.Triangles, idM.Select(i => map[i]).ToArray());
        var ko = Keys(ot, idO);
        return km.SetEquals(ko);
    }

    /// <summary>Failure description: vertices without a partner (model, expected) and triangles only on one side.</summary>
    internal static string Describe(Mesh m, double[][] ov, int[][] ot, double tol = 1e-9)
    {
        bool Near(double[] a, double[] b) => Math.Abs(a[0] - b[0]) <= tol && Math.Abs(a[1] - b[1]) <= tol && Math.Abs(a[2] - b[2]) <= tol;
        var mm = m.Vertices.Select(v => Array.FindIndex(ov, o => Near(o, v))).ToArray();
        int onlyM = mm.Count(i => i < 0), onlyO = ov.Count(o => !m.Vertices.Any(v => Near(o, v)));
        var km = Keys(m.Triangles, mm.Select((i, k) => i < 0 ? -1 - k : i).ToArray());
        var ko = Keys(ot, Enumerable.Range(0, ov.Length).ToArray());
        return $"{m.Triangles.Length}/{ot.Length} triangles, {m.Vertices.Length}/{ov.Length} vertices, {onlyM}/{onlyO} unmatched vertices, {km.Count(t => !ko.Contains(t))}/{ko.Count(t => !km.Contains(t))} triangles only model/reference";
    }

    private static HashSet<(int, int, int)> Keys(int[][] tris, int[] ids)
    {
        var set = new HashSet<(int, int, int)>();
        foreach (var t in tris)
        {
            int a = ids[t[0]], b = ids[t[1]], c = ids[t[2]];
            if (a == b || b == c || a == c) continue;
            if (a <= b && a <= c) set.Add((a, b, c));
            else if (b <= a && b <= c) set.Add((b, c, a));
            else set.Add((c, a, b));
        }
        return set;
    }

    private static (int[] Ids, List<double[]> Reps) Cluster(double[][] v, double tol)
    {
        double h = 4 * tol;
        var cells = new Dictionary<(long, long, long), List<int>>();
        var reps = new List<double[]>();
        var ids = new int[v.Length];
        for (int n = 0; n < v.Length; n++)
        {
            var p = v[n];
            var c = ((long)Math.Floor(p[0] / h), (long)Math.Floor(p[1] / h), (long)Math.Floor(p[2] / h));
            int found = -1;
            for (int dx = -1; dx <= 1 && found < 0; dx++)
                for (int dy = -1; dy <= 1 && found < 0; dy++)
                    for (int dz = -1; dz <= 1 && found < 0; dz++)
                        if (cells.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out var l))
                            foreach (int j in l)
                                if (Math.Max(Math.Abs(reps[j][0] - p[0]), Math.Max(Math.Abs(reps[j][1] - p[1]), Math.Abs(reps[j][2] - p[2]))) <= tol) { found = j; break; }
            if (found < 0)
            {
                found = reps.Count;
                reps.Add(p);
                if (!cells.TryGetValue(c, out var l)) cells[c] = l = new List<int>();
                l.Add(found);
            }
            ids[n] = found;
        }
        return (ids, reps);
    }


    [Fact]
    public void Fixture_covers_the_required_face_classes()
    {
        var solids = Load();
        var targets = solids.SelectMany(s => s.Faces.Where(f => f.Target)).ToList();
        Assert.True(targets.Count >= 250, $"{targets.Count} faces");
        Assert.All(targets, f => Assert.Equal(Lods, f.Reference.Keys.OrderBy(x => x).ToArray()));
        foreach (var kind in new[] { SurfaceKind.Ruled, SurfaceKind.NurbsSurface })
            Assert.True(targets.Count(f => f.Face.SurfaceKind == kind) >= 20, kind.ToString());
        var sources = solids.Select(s => s.Id.Split(':')[0]).Distinct().ToList();
        output.WriteLine($"{targets.Count} target faces in {solids.Count} solids, sources: {string.Join(", ", sources)}");
        Assert.True(sources.Count >= 8);
    }

    private sealed record Tally(int[] Faces, int[] Python, int[] Port, List<string> Lost, List<string> Gained, double Ms, double MaxMs, double PrepMs, int Meshes, int Solids, int Refused);

    /// <summary>All target faces at the four LODs with the extents diagonal chosen by <paramref name="diag"/>; the baseline verdict with the
    /// same diagonal comes from <paramref name="python"/>.</summary>
    private static Tally Run(List<FfSolid> solids, Func<FfSolid, double> diag, Func<FfFace, Dictionary<double, bool>> python)
    {
        var faces = new int[Lods.Length]; var py = new int[Lods.Length]; var port = new int[Lods.Length];
        var lost = new List<string>(); var gained = new List<string>();
        double ms = 0, maxMs = 0, prepMs = 0;
        int meshes = 0, refused = 0;
        foreach (var s in solids)
            for (int li = 0; li < Lods.Length; li++)
            {
                double lod = Lods[li];
                var freeform = s.Faces.Where(f => f.Face.SurfaceKind != SurfaceKind.Plane).Select(f => f.Face).ToList();
                var sw = Stopwatch.StartNew();
                var prepared = FreeformSolid.Create(freeform, lod, diag(s));
                prepMs += sw.Elapsed.TotalMilliseconds;
                foreach (var f in s.Faces.Where(x => x.Target))
                {
                    var (ov, ot) = f.Reference[lod];
                    Mesh? mesh = null;
                    sw.Restart();
                    bool ok = prepared != null && prepared.TryMesh(f.Face, out mesh);
                    sw.Stop();
                    ms += sw.Elapsed.TotalMilliseconds; maxMs = Math.Max(maxMs, sw.Elapsed.TotalMilliseconds); meshes++;
                    if (!ok) refused++;
                    bool exact = ok && Exact(mesh!, ov, ot);
                    bool p = python(f)[lod];
                    faces[li]++; if (p) py[li]++; if (exact) port[li]++;
                    string id = $"{s.Id}:{f.Marker}@{lod}";
                    if (p && !exact && !KnownNotExact.Contains(id)) lost.Add(id);
                    if (!p && exact) gained.Add(id);
                }
            }
        return new Tally(faces, py, port, lost, gained, ms, maxMs, prepMs, meshes, solids.Count * Lods.Length, refused);
    }

    private void Report(string name, Tally t)
    {
        for (int i = 0; i < Lods.Length; i++)
            output.WriteLine($"[{name}] lod {Lods[i]}: {t.Faces[i]} faces, python exact {t.Python[i]} ({100.0 * t.Python[i] / t.Faces[i]:F2}%), port exact {t.Port[i]} ({100.0 * t.Port[i] / t.Faces[i]:F2}%)");
        output.WriteLine($"[{name}] mean {t.Ms / t.Meshes:F4} ms per face and lod (max {t.MaxMs:F2} ms), solid preparation {t.PrepMs / t.Solids:F4} ms per solid and lod, {t.Refused} refused");
        output.WriteLine($"[{name}] exact in the port but not in the prototype: {t.Gained.Count}: {string.Join(", ", t.Gained.Take(30))}");
        output.WriteLine($"[{name}] exact in the prototype but not in the port: {t.Lost.Count}: {string.Join(", ", t.Lost.Take(60))}");
    }

    [Fact]
    public void Freeform_faces_match_the_expected_meshes_where_the_prototype_does()
    {
        var solids = Load();
        // D = diagonal of the bbox of the B-rep edge end points, Dc = of the edge curves
        foreach (var (name, diag, python) in new (string, Func<FfSolid, double>, Func<FfFace, Dictionary<double, bool>>)[]
                 { ("end points", s => s.D, f => f.PythonBrep), ("edge curves", s => s.Dc, f => f.PythonCurve) })
        {
            var t = Run(solids, diag, python);
            Report(name, t);
            Assert.True(t.Lost.Count == 0, $"[{name}] {t.Lost.Count} cases exact in the prototype but not in the port: {string.Join(", ", t.Lost.Take(40))}");
            for (int i = 0; i < Lods.Length; i++) Assert.True(t.Port[i] >= t.Python[i], $"[{name}] lod {Lods[i]}: port {t.Port[i]} < python {t.Python[i]}");
            Assert.Equal(0, t.Refused);
        }
    }

    /// <summary>Optional differential run on a scratch fixture of a larger corpus (RVTIO_FREEFORM_FULL=path of a gz fixture of the same schema).</summary>
    [SkippableFact]
    public void Freeform_full_corpus_when_available()
    {
        string path = TestFiles.FileFromEnvironment("RVTIO_FREEFORM_FULL");
        var solids = Load(path);
        foreach (var (name, diag, python) in new (string, Func<FfSolid, double>, Func<FfFace, Dictionary<double, bool>>)[]
                 { ("end points", s => s.D, f => f.PythonBrep), ("edge curves", s => s.Dc, f => f.PythonCurve) })
        {
            var t = Run(solids, diag, python);
            Report(name, t);
            Assert.True(t.Lost.Count == 0, $"[{name}] {t.Lost.Count} cases exact in the prototype but not in the port: {string.Join(", ", t.Lost.Take(40))}");
        }
    }

    [Fact]
    public void Public_entry_point_gives_the_same_meshes_as_the_prepared_solid()
    {
        var solids = Load();
        int n = 0, same = 0;
        foreach (var s in solids.Where((_, i) => i % 3 == 0))
            foreach (var f in s.Faces.Where(x => x.Target).Take(3))
            {
                var freeform = s.Faces.Where(x => x.Face.SurfaceKind != SurfaceKind.Plane).Select(x => x.Face).ToList();
                var prepared = FreeformSolid.Create(freeform, 0.2, s.D)!;
                bool a = prepared.TryMesh(f.Face, out var m1);
                bool b = FaceMesher.TryMesh(f.Face, 0.2, s.D, out var m2);
                n++;
                Assert.Equal(a, b);
                if (a && m1.Vertices.Length == m2.Vertices.Length && m1.Triangles.Length == m2.Triangles.Length
                    && m1.Triangles.Zip(m2.Triangles).All(t => t.First.SequenceEqual(t.Second))) same++;
            }
        output.WriteLine($"{n} faces through FaceMesher.TryMesh, identical to the prepared solid: {same}");
        Assert.Equal(n, same);
    }

    [Fact]
    public void Comparison_rejects_a_wrong_mesh()
    {
        var s = Load().First(x => x.Faces.Any(f => f.Target && f.PythonBrep[0.2]));
        var f = s.Faces.First(x => x.Target && x.PythonBrep[0.2]);
        Assert.True(FaceMesher.TryMesh(f.Face, 0.2, s.D, out var mesh));
        var (ov, ot) = f.Reference[0.2];
        Assert.True(Exact(mesh, ov, ot));
        Assert.NotEmpty(mesh.Triangles);
        var moved = new Mesh { Vertices = mesh.Vertices.Select((v, i) => i == 0 ? new[] { v[0] + 1e-6, v[1], v[2] } : v).ToArray(), Triangles = mesh.Triangles };
        Assert.False(Exact(moved, ov, ot));
        var fewer = new Mesh { Vertices = mesh.Vertices, Triangles = mesh.Triangles.Skip(1).ToArray() };
        Assert.False(Exact(fewer, ov, ot));
        var flipped = new Mesh { Vertices = mesh.Vertices, Triangles = mesh.Triangles.Select(t => new[] { t[0], t[2], t[1] }).ToArray() };
        Assert.False(Exact(flipped, ov, ot));
    }

    /// <summary>A planar face of a loft cap with the sample points of its curved coedges taken from the freeform edge sampler.</summary>
    private static BrFace Cap(FfFace p, FreeformSolid edges)
    {
        var loops = p.Face.Loops.Select(l => new Loop
        {
            Coedges = l.Coedges.Select(c =>
            {
                var (st, en) = c.OrientToLoop ? (c.V1!, c.V2!) : (c.V2!, c.V1!);
                double[][]? samples = null;
                if (c.Curve!.Kind != CurveKind.Line)
                {
                    samples = edges.EdgeSamples(c.EdgeMarker)!.Select(q => (double[])q.Clone()).ToArray();
                    if (!c.OrientToLoop) Array.Reverse(samples);
                    samples[0] = (double[])st.Clone(); samples[^1] = (double[])en.Clone();
                }
                var pc = FfParamCurve.Create(c.ParamCurve, false)!;
                var (u0, v0) = pc.At(c.ParamCurve!.Lo); var (u1, v1) = pc.At(c.ParamCurve.Hi);
                var (uvs, uve) = c.OrientToLoop ? (new[] { u0, v0 }, new[] { u1, v1 }) : (new[] { u1, v1 }, new[] { u0, v0 });
                return new MeshCoedge { EdgeMarker = c.EdgeMarker, Start = st, End = en, Samples = samples, UvStart = uvs, UvEnd = uve };
            }).ToList(),
        }).ToList();
        return new BrFace { SurfaceKind = SurfaceKind.Plane, Flipped = p.Face.Flipped, Plane = p.Face.Plane, Loops = loops };
    }

    [Theory]
    [InlineData("loft:356811", 106)]
    [InlineData("loft:356812", 90)]
    public void Loft_solids_of_shapes_basic_give_the_reference_triangle_counts(string idPrefix, int trianglesAtLod02)
    {
        var s = Load().Single(x => x.Id.StartsWith(idPrefix + ":"));
        var freeform = s.Faces.Where(f => f.Face.SurfaceKind != SurfaceKind.Plane).Select(f => f.Face).ToList();
        Assert.Equal(2, freeform.Count);
        Assert.Equal(s.D, FreeformSolid.BrepDiagonal(s.Faces.Select(f => f.Face)), 9);
        Assert.Equal(s.Dc, FreeformSolid.CurveDiagonal(s.Faces.Select(f => f.Face)), 9);
        foreach (var (lod, diagonal) in Lods.SelectMany(l => new[] { (l, s.D), (l, s.Dc) }))
        {
            var solid = FreeformSolid.Create(freeform, lod, diagonal)!;
            int total = 0, reference = 0;
            foreach (var f in s.Faces)
            {
                var (ov, ot) = f.Reference[lod];
                Mesh mesh;
                bool ok = f.Face.SurfaceKind == SurfaceKind.Plane
                    ? FaceMesher.TryMesh(Cap(f, solid), lod, diagonal, out mesh)
                    : FaceMesher.TryMesh(f.Face, lod, diagonal, out mesh);
                Assert.True(ok, $"{s.Id} face {f.Marker} lod {lod} refused");
                bool exact = Exact(mesh, ov, ot);
                bool cap = f.Face.SurfaceKind == SurfaceKind.Plane;
                // the ruled faces must be exact at every lod; the planar caps belong to the planar track and are exact at lod 0.1 and 0.2
                if (!cap || lod <= 0.2) Assert.True(exact, $"{s.Id} face {f.Marker} lod {lod} differs from the expected export: {Describe(mesh, ov, ot)}");
                else if (!exact) output.WriteLine($"{s.Id} planar cap {f.Marker} lod {lod}: same vertices, other triangulation ({Describe(mesh, ov, ot)})");
                total += mesh.Triangles.Length; reference += ot.Length;
            }
            output.WriteLine($"{s.Id} lod {lod}: {total} triangles (reference {reference})");
            if (lod <= 0.2) Assert.Equal(reference, total);
            if (lod == 0.2) Assert.Equal(trianglesAtLod02, total);
        }
    }

    [Fact]
    public void Faces_the_rules_do_not_cover_are_refused_without_throwing()
    {
        var s = Load().First(x => x.Faces.Any(f => f.Target && f.Face.SurfaceKind == SurfaceKind.Ruled));
        var f = s.Faces.First(x => x.Target && x.Face.SurfaceKind == SurfaceKind.Ruled).Face;
        Assert.True(FaceMesher.TryMesh(f, 0.2, s.D, out _));
        Assert.False(FaceMesher.TryMesh(f, 0.2, 0.0, out _));
        Assert.False(FaceMesher.TryMesh(f, double.NaN, s.D, out _));
        Assert.False(FaceMesher.TryMesh(f, 0.2, 1e-12, out _)); // a diagonal that would need billions of edge pieces
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.Ruled, Loops = f.Loops }, 0.2, s.D, out _));
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.NurbsSurface, Loops = f.Loops }, 0.2, s.D, out _));
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.Ruled, Ruled = f.Ruled, Envelope = f.Envelope, Loops = Array.Empty<Loop>() }, 0.2, s.D, out _));
        // the envelope of a ruled face is an input (the surface projection of an edge sample is clamped to it)
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.Ruled, Ruled = f.Ruled, OrientToSurface = f.OrientToSurface, Loops = f.Loops }, 0.2, s.D, out _));
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.Ruled, Ruled = f.Ruled, OrientToSurface = f.OrientToSurface, Envelope = new[] { 0.0, 1.0, 0.0 }, Loops = f.Loops }, 0.2, s.D, out _));
        // a coedge without parameter curve, without edge marker, with a broken curve, with a bad or rational parameter curve
        BrFace With(Func<MeshCoedge, MeshCoedge> change) => new()
        {
            SurfaceKind = SurfaceKind.Ruled, Ruled = f.Ruled, OrientToSurface = f.OrientToSurface, Envelope = f.Envelope,
            Loops = f.Loops.Select(l => new Loop { Coedges = l.Coedges.Select((c, i) => i == 0 ? change(c) : c).ToList() }).ToList(),
        };
        MeshCoedge Copy(MeshCoedge c, ParamCurve? pc = null, int marker = int.MinValue, MeshCurve? curve = null) => new()
        {
            EdgeMarker = marker == int.MinValue ? c.EdgeMarker : marker, OrientToLoop = c.OrientToLoop, V1 = c.V1, V2 = c.V2,
            Curve = curve ?? c.Curve, ParamCurve = pc ?? c.ParamCurve,
        };
        Assert.False(FaceMesher.TryMesh(With(c => Copy(c, pc: new ParamCurve { Lo = 0, Hi = 1 })), 0.2, s.D, out _));
        Assert.False(FaceMesher.TryMesh(With(c => Copy(c, marker: -1)), 0.2, s.D, out _));
        Assert.False(FaceMesher.TryMesh(With(c => Copy(c, curve: new MeshCurve { Kind = CurveKind.Line, Lo = 0, Hi = 1 })), 0.2, s.D, out _));
        var pcv = f.Loops[0].Coedges[0].ParamCurve!;
        Assert.False(FaceMesher.TryMesh(With(c => Copy(c, pc: new ParamCurve { Lo = double.NaN, Hi = pcv.Hi, Nurbs = pcv.Nurbs })), 0.2, s.D, out _));
        var rational = new Nurbs { Degree = pcv.Nurbs!.Degree, Knots = pcv.Nurbs.Knots, ControlPoints = pcv.Nurbs.ControlPoints, Weights = pcv.Nurbs.ControlPoints.Select(_ => 1.0).ToArray() };
        Assert.False(FaceMesher.TryMesh(With(c => Copy(c, pc: new ParamCurve { Lo = pcv.Lo, Hi = pcv.Hi, Nurbs = rational })), 0.2, s.D, out _));
    }

    /// <summary>Verdicts of the targets of a solid when every coedge is replaced by <paramref name="change"/> (pcurve, verdict, face-lods).</summary>
    private static (int Same, int Lost, int Gained, int N) Variant(List<FfSolid> solids, Func<MeshCoedge, MeshCoedge> change)
    {
        int n = 0, same = 0, lost = 0, gained = 0;
        foreach (var s in solids)
        {
            var map = new Dictionary<BrFace, BrFace>();
            foreach (var f in s.Faces.Where(x => x.Face.SurfaceKind != SurfaceKind.Plane))
                map[f.Face] = new BrFace
                {
                    SurfaceKind = f.Face.SurfaceKind, OrientToSurface = f.Face.OrientToSurface, Envelope = f.Face.Envelope, Ruled = f.Face.Ruled, Nurbs = f.Face.Nurbs,
                    Loops = f.Face.Loops.Select(l => new Loop { Coedges = l.Coedges.Select(change).ToList() }).ToList(),
                };
            foreach (var lod in Lods)
            {
                var changed = FreeformSolid.Create(map.Values.ToList(), lod, s.D);
                var reference = FreeformSolid.Create(map.Keys.ToList(), lod, s.D)!;
                foreach (var f in s.Faces.Where(x => x.Target))
                {
                    var (ov, ot) = f.Reference[lod];
                    bool a = reference.TryMesh(f.Face, out var m0) && Exact(m0, ov, ot);
                    bool b = changed != null && changed.TryMesh(map[f.Face], out var m1) && Exact(m1, ov, ot);
                    n++; if (a == b) same++; else if (a) lost++; else gained++;
                }
            }
        }
        return (same, lost, gained, n);
    }

    private static (double[] Start, double[] End, double Deviation) Chord(MeshCoedge c)
    {
        var pc = FfParamCurve.Create(c.ParamCurve, false)!;
        var pl = c.ParamCurve!;
        var (u0, v0) = pc.At(pl.Lo); var (u1, v1) = pc.At(pl.Hi);
        double dev = 0;
        for (int k = 1; k < 64; k++)
        {
            double t = pl.Lo + (pl.Hi - pl.Lo) * k / 64.0, a = k / 64.0;
            var (u, v) = pc.At(t);
            dev = Math.Max(dev, Math.Max(Math.Abs(u - (u0 + (u1 - u0) * a)), Math.Abs(v - (v0 + (v1 - v0) * a))));
        }
        return (new[] { u0, v0 }, new[] { u1, v1 }, dev);
    }

    /// <summary>The decoder has no parameter curves, only the uv of the end points of every edge side. Variant 1 replaces every
    /// parameter curve that is a straight uv segment run at constant speed by its linear form; variant 2 gives every edge only the
    /// end point uv as a starting guess and lets the mesher project the 3D samples onto the surface.</summary>
    [Fact]
    public void Parameter_curves_are_derivable_from_the_end_point_uv()
    {
        var solids = Load();
        int pcurves = 0, linear = 0;
        var byKind = new SortedDictionary<string, int[]>();
        foreach (var f in solids.SelectMany(s => s.Faces).Where(f => f.Face.SurfaceKind != SurfaceKind.Plane))
            foreach (var c in f.Face.Loops.SelectMany(l => l.Coedges))
            {
                pcurves++;
                bool straight = Chord(c).Deviation <= 1e-9;
                if (straight) linear++;
                var key = $"{f.Face.SurfaceKind}/{c.Curve!.Kind}";
                if (!byKind.TryGetValue(key, out var cnt)) byKind[key] = cnt = new int[2];
                cnt[0]++; if (straight) cnt[1]++;
            }
        foreach (var (k, cnt) in byKind) output.WriteLine($"  {k}: {cnt[1]} of {cnt[0]} straight");
        MeshCoedge Linear(MeshCoedge c, bool projected)
        {
            var (p0, p1, dev) = Chord(c);
            if (!projected && dev > 1e-9) return c;
            return new MeshCoedge
            {
                EdgeMarker = c.EdgeMarker, OrientToLoop = c.OrientToLoop, Curve = c.Curve, V1 = c.V1, V2 = c.V2,
                ParamCurve = new ParamCurve { Lo = c.ParamCurve!.Lo, Hi = c.ParamCurve.Hi, P0 = p0, P1 = p1, Projected = projected },
            };
        }
        var v1 = Variant(solids, c => Linear(c, false));
        var v2 = Variant(solids, c => Linear(c, true));
        var v3 = Variant(solids, c => new MeshCoedge
        {
            EdgeMarker = c.EdgeMarker, OrientToLoop = c.OrientToLoop, Curve = c.Curve, V1 = c.V1, V2 = c.V2,
            ParamCurve = new ParamCurve { Lo = c.ParamCurve!.Lo, Hi = c.ParamCurve.Hi, Nurbs = c.ParamCurve.Nurbs, Projected = true },
        });
        output.WriteLine($"projection started from the exact parameter curve: {v3.Same} of {v3.N} face-lods keep the verdict, {v3.Lost} lost, {v3.Gained} gained");
        output.WriteLine($"{linear} of {pcurves} parameter curves are straight uv segments");
        output.WriteLine($"linear form of the straight ones: {v1.Same} of {v1.N} face-lods keep the verdict, {v1.Lost} lost, {v1.Gained} gained");
        output.WriteLine($"end point uv + projection for all edges: {v2.Same} of {v2.N} face-lods keep the verdict, {v2.Lost} lost, {v2.Gained} gained");
        Assert.True(linear > pcurves / 2);
        Assert.True(v1.Lost <= 8);
    }


    private static double Hermite1(double p0, double p1, double d0, double d1, double t, double span)
    {
        double t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * span * d0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * span * d1;
    }

    [Fact]
    public void Hermite_curve_nodes_convert_to_the_spline_of_the_reader_formula()
    {
        var rnd = new Random(5);
        double[] t = { 0.0, 1.9, 2.4, 5.0, 6.3 };
        double[][] p = t.Select(_ => new[] { rnd.NextDouble() * 4, rnd.NextDouble() * 4, rnd.NextDouble() * 4 }).ToArray();
        double[][] d = t.Select(_ => new[] { rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5 }).ToArray();
        var curve = FfCurve.Create(new MeshCurve { Kind = CurveKind.Nurbs, Lo = t[0], Hi = t[^1], Nurbs = Hermite.Curve(t, p, d) })!;
        double worst = 0;
        for (int k = 0; k < 400; k++)
        {
            double x = t[0] + (t[^1] - t[0]) * k / 399.0;
            int i = 0;
            while (i + 2 < t.Length && x > t[i + 1]) i++;
            var q = curve.At(x);
            var e = Enumerable.Range(0, 3).Select(c => Hermite1(p[i][c], p[i + 1][c], d[i][c], d[i + 1][c], (x - t[i]) / (t[i + 1] - t[i]), t[i + 1] - t[i])).ToArray();
            worst = Math.Max(worst, Math.Max(Math.Abs(q.X - e[0]), Math.Max(Math.Abs(q.Y - e[1]), Math.Abs(q.Z - e[2]))));
        }
        output.WriteLine($"Hermite curve as B-spline: worst deviation {worst:E2}");
        Assert.True(worst < 1e-12);
        Assert.Null(Hermite.Curve(new[] { 0.0 }, new[] { p[0] }, new[] { d[0] }));
        Assert.Null(Hermite.Curve(new[] { 0.0, 0.0 }, p.Take(2).ToArray(), d.Take(2).ToArray()));
    }

    [Fact]
    public void Hermite_surface_nodes_convert_to_the_spline_of_the_reader_formula()
    {
        var rnd = new Random(9);
        double[] u = { 0.0, 0.25, 0.5, 0.75, 1.0 }, v = { 3.14, 3.5, 3.9 };
        int n = u.Length * v.Length;
        double[] R() => new[] { rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5 };
        var p = Enumerable.Range(0, n).Select(_ => R()).ToArray();
        var du = Enumerable.Range(0, n).Select(_ => R()).ToArray();
        var dv = Enumerable.Range(0, n).Select(_ => R()).ToArray();
        var duv = Enumerable.Range(0, n).Select(_ => R()).ToArray();
        var s = Hermite.Surface(u, v, p, du, dv, duv)!;
        var eval = FfSurfaces.Nurbs(s)!;
        double[] H(double[] a, double[] b, double[] da, double[] db, double t, double span) =>
            Enumerable.Range(0, 3).Select(c => Hermite1(a[c], b[c], da[c], db[c], t, span)).ToArray();
        double worst = 0;
        for (int k = 0; k < 300; k++)
        {
            double x = u[0] + rnd.NextDouble() * (u[^1] - u[0]), y = v[0] + rnd.NextDouble() * (v[^1] - v[0]);
            int i = Math.Min(Array.FindLastIndex(u, q => q <= x), u.Length - 2), j = Math.Min(Array.FindLastIndex(v, q => q <= y), v.Length - 2);
            double su = u[i + 1] - u[i], sv = v[j + 1] - v[j], tu = (x - u[i]) / su, tv = (y - v[j]) / sv;
            int n00 = j * u.Length + i, n10 = n00 + 1, n01 = n00 + u.Length, n11 = n01 + 1;
            var p0 = H(p[n00], p[n10], du[n00], du[n10], tu, su);
            var p1 = H(p[n01], p[n11], du[n01], du[n11], tu, su);
            var d0 = H(dv[n00], dv[n10], duv[n00], duv[n10], tu, su);
            var d1 = H(dv[n01], dv[n11], duv[n01], duv[n11], tu, su);
            var e = H(p0, p1, d0, d1, tv, sv);
            var q = eval(x, y);
            worst = Math.Max(worst, Math.Max(Math.Abs(q.X - e[0]), Math.Max(Math.Abs(q.Y - e[1]), Math.Abs(q.Z - e[2]))));
        }
        output.WriteLine($"Hermite surface as B-spline: worst deviation {worst:E2}");
        Assert.True(worst < 1e-12);
    }

    /// <summary>The kNurbSurface faces are the Hermite surfaces of Revit: the nodes taken from them (value and
    /// derivatives at the distinct knots, by a 5 point stencil) rebuild the control net.</summary>
    [Fact]
    public void Reference_nurbs_surfaces_are_the_hermite_splines_of_their_nodes()
    {
        var faces = Load().SelectMany(s => s.Faces).Where(f => f.Face.SurfaceKind == SurfaceKind.NurbsSurface).Take(12).ToList();
        Assert.NotEmpty(faces);
        double worst = 0;
        foreach (var f in faces)
        {
            var src = f.Face.Nurbs!;
            var eval = FfSurfaces.Nurbs(src)!;
            double[] U = src.KnotsU.Distinct().ToArray(), V = src.KnotsV.Distinct().ToArray();
            const double h = 1e-4;
            // derivative of g at x0 by the 5 point polynomial through the points x0 + k h
            double D1(Func<double, double> g, double x0, double hi)
            {
                int s0 = x0 + 4 * h > hi ? -4 : 0; // one sided and inside one patch, where the spline is a cubic: the stencil is exact
                var xs = Enumerable.Range(0, 5).Select(k => (s0 + k) * h).ToArray();
                var ys = xs.Select(x => g(x0 + x)).ToArray();
                // Lagrange derivative weights at 0
                double sum = 0;
                for (int a = 0; a < 5; a++)
                {
                    double w = 0;
                    for (int m = 0; m < 5; m++)
                    {
                        if (m == a) continue;
                        double prod = 1.0 / (xs[a] - xs[m]);
                        for (int q = 0; q < 5; q++) if (q != a && q != m) prod *= (0 - xs[q]) / (xs[a] - xs[q]);
                        w += prod;
                    }
                    sum += w * ys[a];
                }
                return sum;
            }
            var p = new List<double[]>(); var du = new List<double[]>(); var dv = new List<double[]>(); var duv = new List<double[]>();
            foreach (double y in V)
                foreach (double x in U)
                {
                    var P = eval(x, y);
                    p.Add(new[] { P.X, P.Y, P.Z });
                    double Comp(Pt3 q, int c) => c == 0 ? q.X : c == 1 ? q.Y : q.Z;
                    du.Add(Enumerable.Range(0, 3).Select(c => D1(t => Comp(eval(t, y), c), x, U[^1])).ToArray());
                    dv.Add(Enumerable.Range(0, 3).Select(c => D1(t => Comp(eval(x, t), c), y, V[^1])).ToArray());
                    duv.Add(Enumerable.Range(0, 3).Select(c => D1(s => D1(t => Comp(eval(s, t), c), y, V[^1]), x, U[^1])).ToArray());
                }
            var rebuilt = Hermite.Surface(U, V, p.ToArray(), du.ToArray(), dv.ToArray(), duv.ToArray())!;
            Assert.Equal(src.ControlPoints.Length, rebuilt.ControlPoints.Length);
            Assert.Equal(src.KnotsU, rebuilt.KnotsU);
            Assert.Equal(src.KnotsV, rebuilt.KnotsV);
            for (int i = 0; i < src.ControlPoints.Length; i++)
                for (int c = 0; c < 3; c++) worst = Math.Max(worst, Math.Abs(src.ControlPoints[i][c] - rebuilt.ControlPoints[i][c]));
        }
        output.WriteLine($"{faces.Count} reference surfaces: control net rebuilt from the Hermite nodes, worst deviation {worst:E2} ft");
        Assert.True(worst < 1e-6);
    }

    [Fact]
    public void Diagonal_dependence_is_reported()
    {
        var solids = Load();
        var factors = new[] { 0.98, 0.999, 1 - 1e-9, 1.0, 1 + 1e-9, 1.001, 1.02 }; // of the diagonal of the edge curves
        var exact = new int[factors.Length];
        int n = 0;
        foreach (var s in solids)
        {
            var freeform = s.Faces.Where(f => f.Face.SurfaceKind != SurfaceKind.Plane).Select(f => f.Face).ToList();
            foreach (var lod in new[] { 0.2, 0.4 })
                for (int k = 0; k < factors.Length; k++)
                {
                    var prepared = FreeformSolid.Create(freeform, lod, s.Dc * factors[k]);
                    foreach (var f in s.Faces.Where(x => x.Target))
                    {
                        var (ov, ot) = f.Reference[lod];
                        bool ok = prepared != null && prepared.TryMesh(f.Face, out var mesh) && Exact(mesh, ov, ot);
                        if (k == 0) n++;
                        if (ok) exact[k]++;
                    }
                }
        }
        for (int k = 0; k < factors.Length; k++) output.WriteLine($"D x {factors[k]:R}: {exact[k]} of {n} face-lods exact");
        // a rounding error in D must not change anything
        Assert.Equal(exact[3], exact[2]);
        Assert.Equal(exact[3], exact[4]);
    }
}
