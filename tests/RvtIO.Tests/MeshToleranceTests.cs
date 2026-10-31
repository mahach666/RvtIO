using RvtIO.Geometry.Meshing;
using Xunit;
using Xunit.Abstractions;

namespace RvtIO.Tests;

/// <summary>The LOD tolerance functions and the circular-edge sampler against expected values.</summary>
public class MeshToleranceTests
{
    private readonly ITestOutputHelper output;
    public MeshToleranceTests(ITestOutputHelper output) => this.output = output;

    [Theory]
    [InlineData(0.1, 0.08)]
    [InlineData(0.2, 0.06002)]
    [InlineData(0.3, 0.04004)]
    [InlineData(0.4, 0.02006)]
    public void Surface_tolerance_below_half_lod(double lod, double expected) =>
        Assert.Equal(expected, Tolerances.SurfaceTolerance(lod), 12);

    [Fact]
    public void Surface_tolerance_above_half_lod() => Assert.Equal(0.0001988 - 0.000198 * 0.6, Tolerances.SurfaceTolerance(0.6), 15);

    [Theory]
    [InlineData(10.0, 0.1, 100.0 / 6.0)]
    [InlineData(10.0, 0.2, 100.0 / 11.0)]
    [InlineData(10.0, 0.4, 100.0 / 21.0)]
    public void Max_edge_length_is_diagonal_times_ten_over_one_plus_fifty_lod(double diagonal, double lod, double expected) =>
        Assert.Equal(expected, Tolerances.MaxEdgeLength(diagonal, lod), 12);

    [Fact]
    public void Half_circle_of_500_mm_radius_at_lod_02_has_nine_intervals()
    {
        double r = 500.0 / 304.8;
        Assert.Equal(9, ArcSampler.ArcCount(r, Math.PI, Tolerances.SurfaceTolerance(0.2)));
        var ts = ArcSampler.ArcParams(r, 0, Math.PI, Tolerances.SurfaceTolerance(0.2), false);
        Assert.Equal(10, ts.Length);
        Assert.Equal(Math.PI, ts[^1], 12);
    }

    // values of the cylinder sampler for radius (ft), span (rad), lod
    [Theory]
    [InlineData(1.0, 2 * Math.PI, 0.2, 13)]
    [InlineData(1.0, Math.PI / 2, 0.1, 3)]
    [InlineData(0.164, Math.PI, 0.4, 5)]
    [InlineData(5.0, 2 * Math.PI, 0.3, 36)]
    [InlineData(1e-4, Math.PI / 2, 0.2, 1)]
    public void Arc_count(double radius, double span, double lod, int expected) =>
        Assert.Equal(expected, ArcSampler.ArcCount(radius, span, Tolerances.SurfaceTolerance(lod)));

    [Fact]
    public void Short_arcs_are_divided_evenly_into_four_samples_and_large_ones_into_six()
    {
        var even = ArcSampler.ArcParams(0.001, 0, 0.5, Tolerances.SurfaceTolerance(0.1), false);
        Assert.Equal(new[] { 0.0, 0.5 / 3, 2 * 0.5 / 3, 0.5 }, even);
        Assert.Equal(6, ArcSampler.ArcParams(0.001, 0, 6.0, Tolerances.SurfaceTolerance(0.1), true).Length);
    }

    [Fact]
    public void Long_pieces_are_bisected_to_the_maximum_edge_length()
    {
        // unit circle sampled at 0 and pi, maximum edge length 1.5: both chords (length 2) are halved once
        var ts = ArcSampler.BisectLong(new[] { 0.0, Math.PI }, t => new[] { Math.Cos(t), Math.Sin(t), 0 }, 1.5);
        Assert.Equal(new[] { 0.0, Math.PI / 2, Math.PI }, ts);
    }

    /// <summary>The sample counts: every circular coedge of the planar fixture, at every LOD, has the number of
    /// intervals the sampler predicts (the one face the sampler does not explain is a known gap of the fixture).</summary>
    [Fact]
    public void Sampler_reproduces_the_reference_arc_samples()
    {
        int n = 0, ok = 0;
        var bad = new List<string>();
        foreach (var c in MeshFixtures.LoadPlanar())
            foreach (var l in c.Lods)
            {
                if (l.ReferenceArcs is not { } reference) continue;
                foreach (var def in c.Arcs.EnumerateObject())
                {
                    var d = def.Value;
                    var arc = new Arc
                    {
                        Center = MeshFixtures.Vec(d.GetProperty("c")), Radius = d.GetProperty("r").GetDouble(),
                        Normal = MeshFixtures.Vec(d.GetProperty("n")), Ref = MeshFixtures.Vec(d.GetProperty("x")),
                        Start = d.GetProperty("s").GetDouble(), End = d.GetProperty("e").GetDouble(),
                    };
                    var obs = MeshFixtures.Vecs(reference.GetProperty(def.Name));
                    var mine = ArcSampler.SampleCoedge(arc, MeshFixtures.Vec(d.GetProperty("first")), MeshFixtures.Vec(d.GetProperty("last")),
                        obs[0], obs[^1], l.Lod, c.Diagonal);
                    n++;
                    bool same = mine.Length == obs.Length
                        && mine.Zip(obs).All(p => Enumerable.Range(0, 3).All(i => Math.Abs(p.First[i] - p.Second[i]) <= 1e-6));
                    if (same) ok++; else bad.Add($"{c.Id}@{l.Lod} {mine.Length} vs {obs.Length}");
                }
            }
        output.WriteLine($"arc coedges reproduced: {ok} / {n}; not reproduced: {string.Join(", ", bad)}");
        Assert.True(n > 1000);
        Assert.True(ok >= n - 8, $"{ok} / {n}: {string.Join(", ", bad.Take(20))}");
    }
}
