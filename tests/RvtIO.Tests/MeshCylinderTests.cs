using System.Diagnostics;
using System.Text.Json;
using RvtIO.Geometry.Meshing;
using Xunit;
using Xunit.Abstractions;

namespace RvtIO.Tests;

/// <summary>
/// kCylinder faces against the expected meshes (fixtures/FaceMesh/cylinder.mj.json.gz: faces of synthetic and real projects at
/// 0.1 / 0.2 / 0.3 / 0.4 and, for a few cases, 0.05 ... 0.35). "Exact" = vertex positions within 1e-9 ft and the same triangle
/// multiset as cyclic vertex triples with winding. The baseline verdict per case is stored in the fixture; this port must keep every
/// case the baseline got exact.
/// </summary>
public class MeshCylinderTests
{
    private readonly ITestOutputHelper output;
    public MeshCylinderTests(ITestOutputHelper output) => this.output = output;

    // Cases the baseline gets exact and this port does not ("caseId@lod"). Empty: the baseline pass set is reproduced.
    private static readonly HashSet<string> KnownNotExact = new();

    // Faces that are not exact at one or more levels of detail; the baseline is not exact on them either. Listed so that a new miss is noticed.
    // Causes: 15 faces have NURBS edges (3D curve sampled by the chord-test bisection), 3 have lines and arcs only, 4 have several loops
    // (including two two-patch faces whose second patch has a line end point 1.4e-5 ft off its arc).
    private static readonly HashSet<string> KnownNotExactFaces = new()
    {
        "brep1:g0:f2", "sample_struct_advanced:g567:f12", "sample_struct_advanced:g572:f12",
        "sample_arch_advanced:g1392:f3", "sample_arch_advanced:g3019:f8", "sample_arch_advanced:g3031:f8",
        "sample_arch_basic:g526:f17", "sample_arch_basic:g866:f11", "sample_arch_basic:g866:f14", "sample_arch_basic:g866:f7", "sample_arch_basic:g866:f9",
        "sample_arch_advanced:g1483:f15", "sample_arch_basic:g390:f33", "sample_arch_basic:g418:f12", "sample_arch_basic:g420:f1",
        "sample_arch_basic:g420:f11", "sample_arch_basic:g430:f27", "sample_arch_basic:g526:f19",
        "sample_arch_advanced:g3103:f3", "sample_arch_advanced:g3103:f5", "sample_arch_basic:g381:f14", "sample_lods2:g27:f14",
    };

    private static readonly double[] StandardLods = { 0.1, 0.2, 0.3, 0.4 };

    internal sealed record CylCase(string Id, string[] Stratum, string Span, double Diagonal, JsonElement Cyl, bool OrientToSurface,
        JsonElement Loops, IReadOnlyList<CylLod> Lods);

    internal sealed record CylLod(double Lod, double[][] Vertices, int[][] Triangles, bool Python, bool PythonNoPcurve, string? Error);

    private static List<CylCase>? cached;

    internal static List<CylCase> Load()
    {
        if (cached != null) return cached;
        using var doc = MeshFixtures.Load("cylinder.mj.json.gz");
        var list = doc.RootElement.GetProperty("cases").EnumerateArray().Select(Parse).ToList();
        return cached = list;
    }

    private static double Height(CylinderSurface cyl, double[] p) =>
        (p[0] - cyl.Origin[0]) * cyl.Axis[0] + (p[1] - cyl.Origin[1]) * cyl.Axis[1] + (p[2] - cyl.Origin[2]) * cyl.Axis[2];

    /// <summary>The circular edge a decoder can build from the cylinder frame, the first 3D point of the edge and the cylinder angles
    /// of its end points: the circle of the cylinder at the height of the first point, parameterised by the cylinder angle when the
    /// edge runs towards larger angles (normal = axis) and by the negative angle otherwise (normal = -axis).</summary>
    private static MeshCurve CanonicalArc(CylinderSurface cyl, double[] v1, double[] uvFirst, double[] uvLast)
    {
        double h = Height(cyl, v1);
        bool up = uvLast[1] > uvFirst[1];
        double sign = up ? 1.0 : -1.0;
        double start = sign * uvFirst[1], end = sign * uvLast[1];
        return new MeshCurve
        {
            Kind = CurveKind.Arc, Lo = start, Hi = end, Arc = new Arc
            {
                Center = new[] { cyl.Origin[0] + h * cyl.Axis[0], cyl.Origin[1] + h * cyl.Axis[1], cyl.Origin[2] + h * cyl.Axis[2] },
                Radius = cyl.Radius, Normal = up ? cyl.Axis : cyl.Axis.Select(x => -x).ToArray(), Ref = cyl.Ref, Start = start, End = end,
            },
        };
    }

    private static Nurbs Nurbs(JsonElement e) => new()
    {
        Degree = e.GetProperty("d").GetInt32(), Knots = MeshFixtures.Vec(e.GetProperty("k")), ControlPoints = MeshFixtures.Vecs(e.GetProperty("c")),
    };

    /// <summary>How the parameter curve of a coedge is supplied: the dumped NURBS (Full), a straight line between the two stored end
    /// uv points (Linear: what a decoder with per-side uv samples can build; LinearLinesAndArcs: only for line and arc edges, whose
    /// parameter curve is straight on a cylinder), or not at all (None: the paramOf fallback).</summary>
    internal enum PcMode { Full, Linear, LinearLinesAndArcs, None }

    private static Nurbs Linear(double[] iv, double[] p0, double[] p1) => new()
    {
        Degree = 1, Knots = new[] { iv[0], iv[0], iv[1], iv[1] }, ControlPoints = new[] { p0, p1 },
    };

    /// <summary>The input model of a fixture case.</summary>
    internal static BrFace BuildFace(CylCase c, PcMode mode = PcMode.Full, bool canonicalArcs = false)
    {
        var cy = c.Cyl;
        var ang = MeshFixtures.Vec(cy.GetProperty("ang")); var h = MeshFixtures.Vec(cy.GetProperty("h"));
        var surface = new CylinderSurface
        {
            Radius = cy.GetProperty("r").GetDouble(), Origin = MeshFixtures.Vec(cy.GetProperty("o")), Axis = MeshFixtures.Vec(cy.GetProperty("a")),
            Ref = MeshFixtures.Vec(cy.GetProperty("x")), UScale = cy.GetProperty("us").GetDouble(),
            AngleLo = ang[0], AngleHi = ang[1], HeightLo = h[0], HeightHi = h[1],
        };
        var loops = new List<Loop>();
        foreach (var loop in c.Loops.EnumerateArray())
        {
            var coedges = new List<MeshCoedge>();
            foreach (var e in loop.EnumerateArray())
            {
                var cj = e.GetProperty("c");
                var iv = MeshFixtures.Vec(cj.GetProperty("iv"));
                MeshCurve curve = cj.GetProperty("t").GetString() switch
                {
                    "L" => new MeshCurve { Kind = CurveKind.Line, Lo = iv[0], Hi = iv[1], P0 = MeshFixtures.Vec(cj.GetProperty("p0")), P1 = MeshFixtures.Vec(cj.GetProperty("p1")) },
                    "A" when canonicalArcs && e.TryGetProperty("pc", out var cpc) => CanonicalArc(surface, MeshFixtures.Vec(e.GetProperty("v1")), MeshFixtures.Vec(cpc.GetProperty("p0")), MeshFixtures.Vec(cpc.GetProperty("p1"))),
                    "A" => new MeshCurve
                    {
                        Kind = CurveKind.Arc, Lo = iv[0], Hi = iv[1], Arc = new Arc
                        {
                            Center = MeshFixtures.Vec(cj.GetProperty("arc").GetProperty("c")), Radius = cj.GetProperty("arc").GetProperty("r").GetDouble(),
                            Normal = MeshFixtures.Vec(cj.GetProperty("arc").GetProperty("n")), Ref = MeshFixtures.Vec(cj.GetProperty("arc").GetProperty("x")),
                            Start = cj.GetProperty("arc").GetProperty("s").GetDouble(), End = cj.GetProperty("arc").GetProperty("e").GetDouble(),
                        },
                    },
                    _ => new MeshCurve { Kind = CurveKind.Nurbs, Lo = iv[0], Hi = iv[1], Nurbs = Nurbs(cj.GetProperty("nb")) },
                };
                ParamCurve? pc = null;
                if (mode != PcMode.None && e.TryGetProperty("pc", out var pj))
                {
                    var piv = MeshFixtures.Vec(pj.GetProperty("iv"));
                    var p0 = MeshFixtures.Vec(pj.GetProperty("p0")); var p1 = MeshFixtures.Vec(pj.GetProperty("p1"));
                    bool straight = mode == PcMode.Linear || mode == PcMode.LinearLinesAndArcs && curve.Kind != CurveKind.Nurbs;
                    Nurbs? nb = straight ? Linear(piv, p0, p1) : mode != PcMode.Full ? null : pj.TryGetProperty("nb", out var nj) ? Nurbs(nj) : null;
                    pc = new ParamCurve { Lo = piv[0], Hi = piv[1], P0 = p0, P1 = p1, Nurbs = nb };
                }
                coedges.Add(new MeshCoedge
                {
                    EdgeMarker = e.GetProperty("m").GetInt32(), OrientToLoop = e.GetProperty("ol").GetBoolean(),
                    EdgeOrientToCurve = e.GetProperty("oc").GetBoolean(), V1 = MeshFixtures.Vec(e.GetProperty("v1")), V2 = MeshFixtures.Vec(e.GetProperty("v2")),
                    ParamV1 = e.GetProperty("p1").GetDouble(), ParamV2 = e.GetProperty("p2").GetDouble(), Curve = curve, ParamCurve = pc,
                });
            }
            loops.Add(new Loop { Coedges = coedges });
        }
        return new BrFace { SurfaceKind = SurfaceKind.Cylinder, OrientToSurface = c.OrientToSurface, Cylinder = surface, Loops = loops };
    }

    private static (bool Mesh, bool Set, bool Order, double Ms) Run(CylCase c, CylLod l, PcMode mode = PcMode.Full)
    {
        var face = BuildFace(c, mode);
        var sw = Stopwatch.StartNew();
        bool ok = FaceMesher.TryMesh(face, l.Lod, c.Diagonal, out var mesh);
        sw.Stop();
        if (!ok) return (false, false, false, sw.Elapsed.TotalMilliseconds);
        var (set, order) = MeshFixtures.Compare(mesh, l.Vertices, l.Triangles);
        return (true, set, order, sw.Elapsed.TotalMilliseconds);
    }

    [Fact]
    public void Fixture_covers_the_required_face_classes()
    {
        var cases = Load();
        Assert.True(cases.Count >= 400, $"{cases.Count} faces");
        Assert.True(cases.Count(c => StandardLods.All(lod => c.Lods.Any(l => l.Lod == lod))) >= 400);
        Assert.True(cases.Select(c => c.Id.Split(':')[0]).Distinct().Count() >= 20, "source files");
        foreach (var span in new[] { "half", "quarter", "partial" }) Assert.True(cases.Count(c => c.Span == span) >= 20, span);
        Assert.True(cases.Count(c => c.Stratum[7] == "True") >= 8, "faces with holes");
        Assert.True(cases.Count(c => c.Stratum[3] == "True") >= 20, "faces with NURBS edges");
        Assert.True(cases.Count(c => !c.OrientToSurface) >= 20, "faces not oriented to the surface");
        Assert.True(cases.Count(c => c.Cyl.GetProperty("us").GetDouble() < 0) >= 5, "hole surfaces (negative UScale)");
        Assert.True(cases.Any(c => c.Loops.EnumerateArray().Any(l => l.EnumerateArray().Any(e => !e.TryGetProperty("pc", out var pc) || !pc.TryGetProperty("nb", out _)))),
            "edges without a NURBS parameter curve");
    }

    [Fact]
    public void Cylinder_faces_match_the_expected_meshes_where_the_prototype_does()
    {
        var failures = new List<string>();
        var orderFailures = new List<string>();
        var refused = new List<string>();
        var perLod = new SortedDictionary<double, int[]>(); // n, baseline exact, port exact, port exact incl. order, port refused
        double totalMs = 0, maxMs = 0;
        int meshes = 0;
        foreach (var c in Load())
            foreach (var l in c.Lods)
            {
                var (meshed, set, order, ms) = Run(c, l);
                totalMs += ms; maxMs = Math.Max(maxMs, ms); meshes++;
                if (!perLod.TryGetValue(l.Lod, out var s)) perLod[l.Lod] = s = new int[5];
                s[0]++; if (l.Python) s[1]++; if (set) s[2]++; if (order) s[3]++; if (!meshed) s[4]++;
                string id = $"{c.Id}@{l.Lod}";
                if (l.Python && !set && !KnownNotExact.Contains(id)) failures.Add(id);
                if (!meshed && !l.Python) refused.Add(id);
                if (l.Python && set && !order) orderFailures.Add(id);
            }
        foreach (var (lod, s) in perLod)
            output.WriteLine($"lod {lod}: {s[0]} cases, python exact {s[1]} ({100.0 * s[1] / s[0]:F2}%), port exact {s[2]} ({100.0 * s[2] / s[0]:F2}%), port exact incl. order {s[3]}, port refused {s[4]}");
        output.WriteLine($"mean {totalMs / meshes:F3} ms per face and lod, max {maxMs:F1} ms, {meshes} meshes; vertex or index order differs in {orderFailures.Count} exact cases");
        Assert.True(failures.Count == 0, $"{failures.Count} cases exact in the prototype but not in the port: {string.Join(", ", failures.Take(40))}");
        foreach (var (lod, s) in perLod) Assert.True(s[2] >= s[1], $"lod {lod}: port {s[2]} < python {s[1]}");
    }

    [Fact]
    public void Only_the_documented_faces_are_not_exact()
    {
        var inexact = new HashSet<string>();
        foreach (var c in Load())
            foreach (var l in c.Lods)
                if (!Run(c, l).Set) inexact.Add(c.Id);
        output.WriteLine($"{inexact.Count} faces not exact at some level of detail");
        Assert.True(inexact.Except(KnownNotExactFaces).Count() == 0, "new misses: " + string.Join(", ", inexact.Except(KnownNotExactFaces)));
        Assert.True(KnownNotExactFaces.Except(inexact).Count() == 0, "now exact, remove from the list: " + string.Join(", ", KnownNotExactFaces.Except(inexact)));
    }

    [Fact]
    public void Parameter_curve_fallbacks_are_measured()
    {
        // Full: the dumped NURBS parameter curves. Linear: a straight uv line between the two stored end points of the coedge.
        // None: no parameter curve at all, every sample takes the cylinder paramOf of its 3D point.
        var n = 0; var exact = new Dictionary<PcMode, int>(); var py = new[] { 0, 0 };
        var lost = Enum.GetValues<PcMode>().Where(m => m != PcMode.Full).ToDictionary(m => m, _ => new List<string>());
        foreach (var m in Enum.GetValues<PcMode>()) exact[m] = 0;
        foreach (var c in Load())
            foreach (var l in c.Lods)
            {
                n++; if (l.Python) py[0]++; if (l.PythonNoPcurve) py[1]++;
                bool full = false;
                foreach (var m in Enum.GetValues<PcMode>())
                {
                    bool set = Run(c, l, m).Set;
                    if (set) exact[m]++;
                    if (m == PcMode.Full) full = set;
                    else if (full && !set) lost[m].Add($"{c.Id}@{l.Lod}");
                }
            }
        output.WriteLine($"{n} meshes: python with pcurves {py[0]}, python without {py[1]}; port " + string.Join(", ", exact.Select(e => $"{e.Key} {e.Value}")));
        foreach (var (m, ids) in lost)
            output.WriteLine($"exact with the dumped pcurves but not with {m}: {ids.Count}: {string.Join(", ", ids.Take(12))}");
        Assert.True(exact[PcMode.None] >= py[1], $"port without pcurves {exact[PcMode.None]} < python {py[1]}");
    }

    [Fact]
    public void Arcs_built_from_the_cylinder_frame_are_measured()
    {
        // line and arc faces only: the circle of an arc edge rebuilt from the cylinder frame and the uv of its end points
        // (no dumped arc frame, no dumped parameter curve) against the dumped model
        var rows = new Dictionary<string, int[]>();
        foreach (var c in Load())
        {
            bool nurbs = c.Stratum[3] == "True";
            foreach (var l in c.Lods)
            {
                bool Exact(PcMode m, bool canon) => FaceMesher.TryMesh(BuildFace(c, m, canon), l.Lod, c.Diagonal, out var mesh)
                    && MeshFixtures.Compare(mesh, l.Vertices, l.Triangles).Set;
                var key = nurbs ? "with NURBS edges" : "lines and arcs";
                if (!rows.TryGetValue(key, out var r)) rows[key] = r = new int[6];
                r[0]++; if (l.Python) r[1]++;
                if (Exact(PcMode.Full, false)) r[2]++;
                if (Exact(PcMode.Full, true)) r[3]++;
                if (Exact(PcMode.LinearLinesAndArcs, true)) r[4]++;
                if (Exact(PcMode.None, true)) r[5]++;
            }
        }
        foreach (var (k, r) in rows)
            output.WriteLine($"{k}: {r[0]} meshes, python {r[1]}; dumped model {r[2]}; canonical arcs + dumped pcurves {r[3]}, + linear pcurves {r[4]}, + no pcurves {r[5]}");
        var plain = rows["lines and arcs"];
        Assert.True(plain[4] >= plain[2] * 0.99, $"derived model {plain[4]} of {plain[2]}");
    }

    [Fact]
    public void Column_halves_have_the_documented_segment_counts()
    {
        // D1000 mm x h2000 mm round column: each cylinder half is 9 segments at 0.1 / 0.2 (18 around the column), then 33 / 38 and 49 / 62
        var halves = Load().Where(c => Math.Abs(c.Cyl.GetProperty("r").GetDouble() - 0.5 / 0.3048) < 1e-6
            && Math.Abs(MeshFixtures.Vec(c.Cyl.GetProperty("h")) is var h ? h[1] - h[0] - 2 / 0.3048 : 1) < 1e-3).ToList();
        Assert.True(halves.Count >= 2, "column halves in the fixture");
        var expected = new Dictionary<double, (int V, int T)> { [0.1] = (18, 16), [0.2] = (20, 18), [0.3] = (33, 38), [0.4] = (49, 62) };
        foreach (var c in halves)
            foreach (var (lod, (v, t)) in expected)
            {
                var l = c.Lods.Single(x => x.Lod == lod);
                Assert.True(FaceMesher.TryMesh(BuildFace(c), lod, c.Diagonal, out var mesh), $"{c.Id}@{lod}");
                Assert.Equal((v, t), (mesh.Vertices.Length, mesh.Triangles.Length));
                Assert.Equal((v, t), (l.Vertices.Length, l.Triangles.Length));
                Assert.True(MeshFixtures.Compare(mesh, l.Vertices, l.Triangles).Set, $"{c.Id}@{lod}");
            }
        output.WriteLine($"{halves.Count} column halves: vertices/triangles per half {string.Join(", ", expected.Select(e => $"{e.Key}: {e.Value.V}/{e.Value.T}"))}");
    }

    [Fact]
    public void Faces_the_rules_do_not_cover_are_refused_without_throwing()
    {
        var c = Load().First(x => x.Span == "half" && x.Stratum[7] == "False");
        var l = c.Lods.First(x => x.Lod == 0.2);
        var good = BuildFace(c);
        Assert.True(FaceMesher.TryMesh(good, l.Lod, c.Diagonal, out _));
        Assert.False(FaceMesher.TryMesh(good, l.Lod, 0.0, out _));
        Assert.False(FaceMesher.TryMesh(good, double.NaN, c.Diagonal, out _));
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.Cylinder, Loops = good.Loops }, l.Lod, c.Diagonal, out _));
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.Cylinder, Cylinder = good.Cylinder, Loops = Array.Empty<Loop>() }, l.Lod, c.Diagonal, out _));
        var noCurve = new BrFace
        {
            SurfaceKind = SurfaceKind.Cylinder, Cylinder = good.Cylinder,
            Loops = new[] { new Loop { Coedges = new[] { new MeshCoedge { V1 = new[] { 0.0, 0, 0 }, V2 = new[] { 1.0, 0, 0 } } } } },
        };
        Assert.False(FaceMesher.TryMesh(noCurve, l.Lod, c.Diagonal, out _));
        var badRadius = new BrFace
        {
            SurfaceKind = SurfaceKind.Cylinder, Loops = good.Loops,
            Cylinder = new CylinderSurface
            {
                Radius = 0.0, Origin = good.Cylinder!.Origin, Axis = good.Cylinder.Axis, Ref = good.Cylinder.Ref, UScale = 1.0,
                AngleLo = 0, AngleHi = 1, HeightLo = 0, HeightHi = 1,
            },
        };
        Assert.False(FaceMesher.TryMesh(badRadius, l.Lod, c.Diagonal, out _));
    }

    /// <summary>Optional differential run on a large scratch fixture (RVTIO_CYL_FULL=path of a gz fixture of the same schema).</summary>
    [SkippableFact]
    public void Cylinder_full_corpus_when_available()
    {
        string path = TestFiles.FileFromEnvironment("RVTIO_CYL_FULL");
        using var doc = MeshFixtures.Load(path);
        var modes = Enum.GetValues<PcMode>();
        var perLod = new SortedDictionary<double, int[]>(); // n, baseline, baseline without pcurves, port per mode, refused
        var lostIds = new List<string>();
        double ms = 0; int meshes = 0;
        foreach (var jc in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var c = Parse(jc);
            foreach (var l in c.Lods)
            {
                if (!perLod.TryGetValue(l.Lod, out var s)) perLod[l.Lod] = s = new int[4 + modes.Length];
                s[0]++; if (l.Python) s[1]++; if (l.PythonNoPcurve) s[2]++;
                foreach (var m in modes)
                {
                    var r = Run(c, l, m);
                    if (m == PcMode.Full) { ms += r.Ms; meshes++; if (l.Python && !r.Set) lostIds.Add($"{c.Id}@{l.Lod}"); if (!r.Mesh) s[3 + modes.Length]++; }
                    if (r.Set) s[3 + (int)m]++;
                }
            }
        }
        foreach (var (lod, s) in perLod)
            output.WriteLine($"lod {lod}: {s[0]} meshes, python {s[1]} ({100.0 * s[1] / s[0]:F2}%) / without pcurves {s[2]}; port " +
                string.Join(", ", modes.Select(m => $"{m} {s[3 + (int)m]} ({100.0 * s[3 + (int)m] / s[0]:F2}%)")) + $"; refused {s[3 + modes.Length]}");
        output.WriteLine($"full corpus: mean {ms / meshes:F3} ms per face and lod; python exact but port not: {lostIds.Count} {string.Join(", ", lostIds.Take(30))}");
        Assert.True(lostIds.Count == 0, $"{lostIds.Count} meshes lost");
    }

    private static CylCase Parse(JsonElement c) => new(c.GetProperty("id").GetString()!, c.GetProperty("stratum").EnumerateArray().Select(x => x.ToString()).ToArray(),
        c.GetProperty("span").GetString()!, c.GetProperty("D").GetDouble(), c.GetProperty("cyl").Clone(), c.GetProperty("ots").GetBoolean(),
        c.GetProperty("loops").Clone(), c.GetProperty("lods").EnumerateArray().Select(l => new CylLod(
            l.GetProperty("lod").GetDouble(), MeshFixtures.Vecs(l.GetProperty("v")),
            l.GetProperty("t").EnumerateArray().Select(t => t.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray(),
            l.GetProperty("py").GetInt32() == 1, l.GetProperty("pyNoPc").GetInt32() == 1,
            l.GetProperty("err").ValueKind == JsonValueKind.String ? l.GetProperty("err").GetString() : null)).ToList());
}
