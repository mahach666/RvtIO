using RvtIO.Geometry.Meshing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The convex decomposition + fan against the expected fans (fixtures/FaceMesh/getess.json.gz: random polygons with holes,
/// pinched polygons and the polygons the planar mesher produced for real faces).</summary>
public class MeshTessTests
{
    [Fact]
    public void Triangulate_profile_reproduces_the_research_fans()
    {
        using var doc = MeshFixtures.Load("getess.json.gz");
        var cases = doc.RootElement.GetProperty("cases").EnumerateArray().ToList();
        Assert.True(cases.Count >= 400);
        var failures = new List<string>();
        int n = 0;
        foreach (var c in cases)
        {
            var pts = c.GetProperty("pts").EnumerateArray().Select(p => (X: p[0].GetDouble(), Y: p[1].GetDouble())).ToArray();
            var loops = c.GetProperty("loops").EnumerateArray().Select(l => l.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToList();
            var expected = c.GetProperty("fans").EnumerateArray().Select(t => (t[0].GetInt32(), t[1].GetInt32(), t[2].GetInt32())).ToList();
            // the fixture polygons are regions (outer first, holes after): the pieces are the same as the profile's
            var actual = GeTess.ConvexPieces(pts, loops)
                .SelectMany(p => Enumerable.Range(1, p.Length - 2).Select(j => (p[0], p[j], p[j + 1]))).ToList();
            if (!expected.SequenceEqual(actual)) failures.Add($"#{n} {c.GetProperty("kind").GetString()}");
            n++;
        }
        Assert.True(failures.Count == 0, $"{failures.Count} of {cases.Count}: {string.Join(", ", failures.Take(30))}");
    }

    [Fact]
    public void Triangulate_profile_assigns_holes_to_the_enclosing_outer_loop()
    {
        var pts = new (double X, double Y)[]
        {
            (0, 0), (10, 0), (10, 10), (0, 10), // outer, counter-clockwise
            (2, 2), (2, 4), (4, 4), (4, 2),     // hole, clockwise
            (20, 0), (30, 0), (30, 10), (20, 10),
        };
        var tris = GeTess.TriangulateProfile(pts, new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 8, 9, 10, 11 } });
        double Area((int A, int B, int C) t) => Math.Abs((pts[t.B].X - pts[t.A].X) * (pts[t.C].Y - pts[t.A].Y) - (pts[t.C].X - pts[t.A].X) * (pts[t.B].Y - pts[t.A].Y)) / 2;
        Assert.Equal(100 - 4 + 100, tris.Sum(Area), 9);
        Assert.All(tris, t => Assert.True(Area(t) > 0));
    }

    [Fact]
    public void Triangulate_profile_keeps_an_island_inside_a_concentric_hole()
    {
        // outer 10 x 10, a hole 8 x 8 (clockwise) and an island 6 x 6 (counter-clockwise) in the same centre: the centre of the hole lies in the island,
        // yet the hole belongs to the outer loop
        var pts = new (double X, double Y)[]
        {
            (0, 0), (10, 0), (10, 10), (0, 10),
            (1, 1), (1, 9), (9, 9), (9, 1),
            (2, 2), (8, 2), (8, 8), (2, 8),
        };
        var tris = GeTess.TriangulateProfile(pts, new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 8, 9, 10, 11 } });
        double Area((int A, int B, int C) t) => Math.Abs((pts[t.B].X - pts[t.A].X) * (pts[t.C].Y - pts[t.A].Y) - (pts[t.C].X - pts[t.A].X) * (pts[t.B].Y - pts[t.A].Y)) / 2;
        Assert.Equal(100 - 64 + 36, tris.Sum(Area), 9);
    }
}
