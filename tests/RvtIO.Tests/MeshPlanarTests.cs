using System.Diagnostics;
using RvtIO.Geometry.Meshing;
using Xunit;
using Xunit.Abstractions;

namespace RvtIO.Tests;

/// <summary>
/// Planar faces against the expected meshes (fixtures/FaceMesh/planar.mj.json.gz: faces of synthetic and real projects at the
/// four LODs). "Exact" = vertex positions within 1e-9 ft and the same triangle multiset as cyclic vertex triples with winding.
/// The baseline verdict per case is stored in the fixture; this port must keep every case the baseline got exact.
/// </summary>
public class MeshPlanarTests
{
    private readonly ITestOutputHelper output;
    public MeshPlanarTests(ITestOutputHelper output) => this.output = output;

    // Cases the baseline gets exact and this port does not ("caseId@lod"). Empty: the baseline pass set is reproduced.
    private static readonly HashSet<string> KnownNotExact = new();

    private static readonly double[] Lods = { 0.1, 0.2, 0.3, 0.4 };

    private static (bool Set, bool Order, double Ms) Run(MeshFixtures.PlanarCase c, MeshFixtures.PlanarLod l,
        Func<string, double[], double[], double[][]?> curved)
    {
        var face = MeshFixtures.BuildFace(c, curved);
        var sw = Stopwatch.StartNew();
        bool ok = FaceMesher.TryMesh(face, l.Lod, c.Diagonal, out var mesh);
        sw.Stop();
        if (!ok) return (false, false, sw.Elapsed.TotalMilliseconds);
        var (set, order) = MeshFixtures.Compare(mesh, l.Vertices, l.Triangles);
        return (set, order, sw.Elapsed.TotalMilliseconds);
    }

    private static Func<string, double[], double[], double[][]?> ReferenceArcs(MeshFixtures.PlanarLod l) =>
        (key, _, _) => l.ReferenceArcs is { } arcs && arcs.TryGetProperty(key, out var a) ? MeshFixtures.Vecs(a) : null;

    [Fact]
    public void Fixture_covers_the_required_face_classes()
    {
        var cases = MeshFixtures.LoadPlanar();
        Assert.True(cases.Count >= 400, $"{cases.Count} faces");
        foreach (var cat in new[] { "rect", "rect-multicell", "concave", "holes", "flipped", "gap", "arc", "hard" })
            Assert.True(cases.Count(c => c.Categories.Contains(cat)) >= 20, cat);
        Assert.All(cases, c => Assert.Equal(Lods, c.Lods.Select(l => l.Lod).ToArray()));
        Assert.True(cases.Select(c => c.Id.Split(':')[0]).Distinct().Count() >= 20, "source files");
    }

    [Fact]
    public void Planar_faces_match_the_expected_meshes_where_the_prototype_does()
    {
        var cases = MeshFixtures.LoadPlanar();
        var failures = new List<string>();
        var orderFailures = new List<string>();
        var perLod = Lods.ToDictionary(x => x, _ => new int[5]); // n, baseline exact, port exact, port exact order, baseline exact order
        double totalMs = 0, maxMs = 0;
        int faces = 0;
        foreach (var c in cases)
            foreach (var l in c.Lods)
            {
                var (set, order, ms) = Run(c, l, ReferenceArcs(l));
                totalMs += ms; maxMs = Math.Max(maxMs, ms); faces++;
                var s = perLod[l.Lod];
                s[0]++; if (l.Python > 0) s[1]++; if (set) s[2]++; if (order) s[3]++; if (l.Python == 2) s[4]++;
                string id = $"{c.Id}@{l.Lod}";
                if (l.Python > 0 && !set && !KnownNotExact.Contains(id)) failures.Add(id);
                if (l.Python == 2 && set && !order) orderFailures.Add(id);
            }
        foreach (var (lod, s) in perLod)
            output.WriteLine($"lod {lod}: {s[0]} cases, python exact {s[1]} ({100.0 * s[1] / s[0]:F2}%), port exact {s[2]} ({100.0 * s[2] / s[0]:F2}%), port exact incl. order {s[3]} (python {s[4]})");
        output.WriteLine($"mean {totalMs / faces:F3} ms per face and lod, max {maxMs:F1} ms, {faces} meshes");
        Assert.True(failures.Count == 0, $"{failures.Count} cases exact in the prototype but not in the port: {string.Join(", ", failures.Take(40))}");
        foreach (var (lod, s) in perLod) Assert.True(s[2] >= s[1], $"lod {lod}: port {s[2]} < python {s[1]}");
        Assert.True(orderFailures.Count == 0, $"{orderFailures.Count} cases differ in vertex or index order: {string.Join(", ", orderFailures.Take(40))}");
    }

    [Fact]
    public void Planar_faces_with_arc_sampler_points_match_the_prototype()
    {
        var cases = MeshFixtures.LoadPlanar().Where(c => c.Arcs.EnumerateObject().Any()).ToList();
        Assert.True(cases.Count >= 50);
        var failures = new List<string>();
        int n = 0, python = 0, port = 0;
        foreach (var c in cases)
            foreach (var l in c.Lods)
            {
                Func<string, double[], double[], double[][]?> sampled = (key, start, end) =>
                {
                    var d = c.Arcs.GetProperty(key);
                    var arc = new Arc
                    {
                        Center = MeshFixtures.Vec(d.GetProperty("c")), Radius = d.GetProperty("r").GetDouble(),
                        Normal = MeshFixtures.Vec(d.GetProperty("n")), Ref = MeshFixtures.Vec(d.GetProperty("x")),
                        Start = d.GetProperty("s").GetDouble(), End = d.GetProperty("e").GetDouble(),
                    };
                    return ArcSampler.SampleCoedge(arc, MeshFixtures.Vec(d.GetProperty("first")), MeshFixtures.Vec(d.GetProperty("last")),
                        start, end, l.Lod, c.Diagonal);
                };
                var (set, _, _) = Run(c, l, sampled);
                n++; if (l.PythonWithSampler > 0) python++; if (set) port++;
                if (l.PythonWithSampler > 0 && !set) failures.Add($"{c.Id}@{l.Lod}");
            }
        output.WriteLine($"{n} arc-bounded meshes with sampler points: prototype exact {python}, port exact {port}");
        Assert.True(failures.Count == 0, $"{failures.Count} cases: {string.Join(", ", failures.Take(40))}");
        Assert.True(port >= python);
    }

    /// <summary>Optional differential run on a large scratch fixture (RVTIO_MESH_FULL=path of a gz fixture of the same schema).</summary>
    [SkippableFact]
    public void Planar_full_corpus_when_available()
    {
        string path = TestFiles.FileFromEnvironment("RVTIO_MESH_FULL");
        int n = 0, py = 0, port = 0, lost = 0;
        var lostIds = new List<string>();
        foreach (var c in MeshFixtures.LoadPlanar(path))
            foreach (var l in c.Lods)
            {
                var (set, _, _) = Run(c, l, ReferenceArcs(l));
                n++; if (l.Python > 0) py++; if (set) port++;
                if (l.Python > 0 && !set) { lost++; lostIds.Add($"{c.Id}@{l.Lod}"); }
            }
        output.WriteLine($"full corpus {n} meshes: python exact {py}, port exact {port}, lost {lost}: {string.Join(", ", lostIds.Take(30))}");
        // Faces 4 and 7 of one model: collinear points with 1e-14 noise, decided by the last ulp of the dot products (numpy vs scalar)
        Assert.True(lost <= 4, $"{lost} meshes lost");
    }

    [Fact]
    public void Faces_the_rules_do_not_cover_are_refused_without_throwing()
    {
        var c = MeshFixtures.LoadPlanar().First(x => x.Categories.Contains("rect"));
        var l = c.Lods[1];
        var good = MeshFixtures.BuildFace(c, (_, _, _) => null);
        Assert.True(FaceMesher.TryMesh(good, l.Lod, c.Diagonal, out _));
        Assert.False(FaceMesher.TryMesh(good, l.Lod, 0.0, out _));
        Assert.False(FaceMesher.TryMesh(good, double.NaN, c.Diagonal, out _));
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.Cylinder, Loops = good.Loops }, l.Lod, c.Diagonal, out _));
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.Plane, Loops = good.Loops }, l.Lod, c.Diagonal, out _));
        Assert.False(FaceMesher.TryMesh(new BrFace { SurfaceKind = SurfaceKind.Plane, Plane = good.Plane, Loops = Array.Empty<Loop>() }, l.Lod, c.Diagonal, out _));
        var broken = new BrFace
        {
            SurfaceKind = SurfaceKind.Plane, Plane = good.Plane,
            Loops = new[] { new Loop { Coedges = new[] { new MeshCoedge { Start = new[] { 0.0, 0.0, 0.0 }, End = new[] { double.NaN, 0, 0 } } } } },
        };
        Assert.False(FaceMesher.TryMesh(broken, l.Lod, c.Diagonal, out _));
    }
}
