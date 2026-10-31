using System.IO.Compression;
using System.Text.Json;
using RvtIO.Geometry.Meshing;

namespace RvtIO.Tests;

/// <summary>Loading of the face mesher fixtures (tests/RvtIO.Tests/Fixtures/FaceMesh) and the comparison rule used against them.</summary>
internal static class MeshFixtures
{
    internal const double Tolerance = 1e-9;

    internal static JsonDocument Load(string name)
    {
        if (Path.IsPathRooted(name))
        {
            using var f = File.OpenRead(name);
            using var g = new GZipStream(f, CompressionMode.Decompress);
            return JsonDocument.Parse(g);
        }
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string path = Path.Combine(dir, "tests", "RvtIO.Tests", "Fixtures", "FaceMesh", name);
            if (File.Exists(path))
            {
                using var file = File.OpenRead(path);
                using var gz = new GZipStream(file, CompressionMode.Decompress);
                return JsonDocument.Parse(gz);
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("fixture not found: " + name);
    }

    internal static double[] Vec(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();

    internal static double[][] Vecs(JsonElement e) => e.EnumerateArray().Select(Vec).ToArray();

    internal sealed record PlanarCase(string Id, string[] Categories, bool Flipped, double Diagonal, JsonElement Plane,
        JsonElement Loops, JsonElement Arcs, IReadOnlyList<PlanarLod> Lods);

    internal sealed record PlanarLod(double Lod, double[][] Vertices, int[][] Triangles, int Python, int PythonBeforeFix,
        int? PythonWithSampler, JsonElement? ReferenceArcs);

    internal static List<PlanarCase> LoadPlanar(string name = "planar.mj.json.gz")
    {
        using var doc = Load(name);
        var list = new List<PlanarCase>();
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var lods = c.GetProperty("lods").EnumerateArray().Select(l => new PlanarLod(
                l.GetProperty("lod").GetDouble(), Vecs(l.GetProperty("v")),
                l.GetProperty("t").EnumerateArray().Select(t => t.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray(),
                l.GetProperty("py").GetInt32(), l.GetProperty("po").GetInt32(),
                l.TryGetProperty("pyS", out var ps) ? ps.GetInt32() : null,
                l.TryGetProperty("arcs", out var arcs) ? arcs.Clone() : null)).ToList();
            list.Add(new PlanarCase(c.GetProperty("id").GetString()!, c.GetProperty("cat").EnumerateArray().Select(x => x.GetString()!).ToArray(),
                c.GetProperty("flipped").GetBoolean(), c.GetProperty("D").GetDouble(), c.GetProperty("plane").Clone(),
                c.GetProperty("loops").Clone(), c.GetProperty("arcdefs").Clone(), lods));
        }
        return list;
    }

    /// <summary>The input model of a planar fixture case. <paramref name="curved"/> supplies the sample points of curved coedges
    /// (key "loop.coedge") or is null for the straight faces.</summary>
    internal static BrFace BuildFace(PlanarCase c, Func<string, double[], double[], double[][]?> curved)
    {
        var plane = new PlaneSurface { Origin = Vec(c.Plane.GetProperty("o")), U = Vec(c.Plane.GetProperty("u")), V = Vec(c.Plane.GetProperty("v")) };
        var loops = new List<Loop>();
        int li = 0;
        foreach (var loop in c.Loops.EnumerateArray())
        {
            var coedges = new List<MeshCoedge>();
            int ci = 0;
            foreach (var co in loop.EnumerateArray())
            {
                var start = Vec(co.GetProperty("s")); var end = Vec(co.GetProperty("t"));
                double[]? uvs = null, uve = null;
                if (co.GetProperty("uv").ValueKind == JsonValueKind.Array)
                {
                    var uv = Vecs(co.GetProperty("uv"));
                    uvs = uv[0]; uve = uv[1];
                }
                double[][]? samples = co.TryGetProperty("k", out _) ? curved($"{li}.{ci}", start, end) : null;
                coedges.Add(new MeshCoedge
                {
                    EdgeMarker = co.GetProperty("e").GetInt32(), Start = start, End = end, UvStart = uvs, UvEnd = uve, Samples = samples,
                });
                ci++;
            }
            loops.Add(new Loop { Coedges = coedges });
            li++;
        }
        return new BrFace { SurfaceKind = SurfaceKind.Plane, Flipped = c.Flipped, Plane = plane, Loops = loops };
    }

    private static int[] Canon(int a, int b, int c)
    {
        if (a <= b && a <= c) return new[] { a, b, c };
        if (b <= a && b <= c) return new[] { b, c, a };
        return new[] { c, a, b };
    }

    /// <summary>(exact set, exact order). Set: the vertices match by position within the tolerance on every coordinate and the
    /// triangles, as cyclic vertex triples (winding kept), are the same multiset. Order: additionally the vertex list and every
    /// index triple are identical.</summary>
    internal static (bool Set, bool Order) Compare(Mesh pred, double[][] obsV, int[][] obsT)
    {
        var all = obsV.Concat(pred.Vertices).ToList();
        var ids = Classes(all);
        var oc = ids.Take(obsV.Length).ToArray();
        var pc = ids.Skip(obsV.Length).ToArray();
        var ob = obsT.Select(t => Canon(oc[t[0]], oc[t[1]], oc[t[2]])).OrderBy(t => t[0]).ThenBy(t => t[1]).ThenBy(t => t[2]).ToList();
        var pr = pred.Triangles.Select(t => Canon(pc[t[0]], pc[t[1]], pc[t[2]])).OrderBy(t => t[0]).ThenBy(t => t[1]).ThenBy(t => t[2]).ToList();
        bool set = ob.Count == pr.Count && ob.Zip(pr).All(p => p.First.SequenceEqual(p.Second));
        bool order = set && pred.Vertices.Length == obsV.Length
            && pred.Vertices.Zip(obsV).All(p => Enumerable.Range(0, 3).All(i => Math.Abs(p.First[i] - p.Second[i]) <= Tolerance))
            && pred.Triangles.Zip(obsT).All(p => p.First.SequenceEqual(p.Second));
        return (set, order);
    }

    private static int[] Classes(List<double[]> points)
    {
        const double cell = 1e-6;
        var grid = new Dictionary<(long, long, long), List<int>>();
        var reps = new List<double[]>();
        var ids = new int[points.Count];
        for (int n = 0; n < points.Count; n++)
        {
            var p = points[n];
            var key = ((long)Math.Floor(p[0] / cell), (long)Math.Floor(p[1] / cell), (long)Math.Floor(p[2] / cell));
            int found = -1;
            for (int dx = -1; dx <= 1 && found < 0; dx++)
                for (int dy = -1; dy <= 1 && found < 0; dy++)
                    for (int dz = -1; dz <= 1 && found < 0; dz++)
                        if (grid.TryGetValue((key.Item1 + dx, key.Item2 + dy, key.Item3 + dz), out var list))
                            foreach (int j in list)
                                if (Math.Abs(reps[j][0] - p[0]) <= Tolerance && Math.Abs(reps[j][1] - p[1]) <= Tolerance
                                    && Math.Abs(reps[j][2] - p[2]) <= Tolerance) { found = j; break; }
            if (found < 0)
            {
                found = reps.Count;
                reps.Add(p);
                if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>();
                l.Add(found);
            }
            ids[n] = found;
        }
        return ids;
    }
}
