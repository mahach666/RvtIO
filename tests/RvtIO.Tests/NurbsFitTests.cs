using RvtIO.Geometry.Meshing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The 3D edge curves of the fixtures that are not lines or circles are the natural cubic interpolation of the edge samples
/// (chord length knots): fitting the samples recovered from a dumped curve gives back its knots and control points.</summary>
public class NurbsFitTests
{
    [Fact]
    public void TheDumpedNurbsEdgesAreTheNaturalInterpolationOfTheirSamples()
    {
        using var doc = MeshFixtures.Load("cylinder.mj.json.gz");
        int checkedCurves = 0;
        foreach (var fcase in doc.RootElement.GetProperty("cases").EnumerateArray())
            foreach (var loop in fcase.GetProperty("loops").EnumerateArray())
                foreach (var edge in loop.EnumerateArray())
                {
                    var curve = edge.GetProperty("c");
                    if (curve.GetProperty("t").GetString() != "N") continue;
                    var nb = curve.GetProperty("nb");
                    var dumped = new Nurbs
                    {
                        Degree = nb.GetProperty("d").GetInt32(),
                        Knots = MeshFixtures.Vec(nb.GetProperty("k")),
                        ControlPoints = MeshFixtures.Vecs(nb.GetProperty("c")),
                    };
                    if (dumped.Degree != 3 || dumped.Knots.Length != dumped.ControlPoints.Length + 4) continue;
                    // the samples are the curve at the distinct breakpoints
                    var breaks = new List<double> { dumped.Knots[3] };
                    breaks.AddRange(dumped.Knots.Skip(4).Take(dumped.Knots.Length - 8));
                    breaks.Add(dumped.Knots[^4]);
                    var samples = breaks.Select(t => NurbsEval.Point3d(dumped, t)).ToList();
                    var fitted = NurbsFit.Fit(samples);
                    if (fitted == null) throw new Xunit.Sdk.XunitException($"null: n={samples.Count} first={string.Join(',', samples[0])} second={string.Join(',', samples[1])} last={string.Join(',', samples[^1])} knots={string.Join(' ', dumped.Knots)}");
                    Assert.Equal(dumped.ControlPoints.Length, fitted!.ControlPoints.Length);
                    for (int i = 0; i < dumped.Knots.Length; i++) Assert.Equal(dumped.Knots[i], fitted.Knots[i], 9);
                    for (int i = 0; i < dumped.ControlPoints.Length; i++)
                        for (int k = 0; k < 3; k++) Assert.Equal(dumped.ControlPoints[i][k], fitted.ControlPoints[i][k], 8);
                    checkedCurves++;
                }
        Assert.True(checkedCurves > 100, $"only {checkedCurves} curves checked");
    }

    [Fact]
    public void FewerThanThreeDistinctSamplesAreNotFitted()
    {
        Assert.Null(NurbsFit.Fit([[0, 0, 0], [1, 0, 0]]));
        Assert.Equal(2, NurbsFit.Distinct([[0, 0, 0], [0, 0, 0], [1, 0, 0]]).Count);
    }
}
