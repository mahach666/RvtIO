using RvtIO.Geometry.Meshing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The three point circle against recorded arcs of arc edges without an active cylinder beside them:
/// first point, first interior point, last point of the edge and the centre, reference vector, normal, end angle and radius of the expected arc, all
/// to the last bit. The half circles among them are the ones where one rounding error moves the interval count.</summary>
public class ThreePointArcTests
{
    private static readonly (double[] P1, double[] P2, double[] P3, double[] Center, double[] Ref, double[] Normal, double End, double Radius)[] Cases =
    {
        (new double[] { 1.8448115977677502e-15, -0.8877554766958896, 1.8623588451416542 }, new double[] { 0.12166023400588628, -0.8877554766958896, 1.8430897570688796 }, new double[] { -1.2720823420729e-15, -0.8877554766958896, 1.0749572703385064 },
         new double[] { 3.5388358909926865e-16, -0.8877554766958896, 1.4686580577400805 }, new double[] { 3.786957142017954e-15, 0, 1 }, new double[] { 0, 1, 0 }, 3.141592653589793, 0.39370078740157366),
        (new double[] { 3.285630467495597, -2.668941146617645, 8.032390071677739 }, new double[] { 3.057251197263265, -2.649404633644246, 8.260769341910073 }, new double[] { 3.285630467495519, 2.6900427020050324, 8.032390071677723 },
         new double[] { 3.2856304674955576, 0.010550777693694165, 8.03239007167773 }, new double[] { 1.452301203098731e-14, -1, 3.0470257107619577e-15 }, new double[] { -0.7071067811865521, -1.2423892832894533e-14, -0.7071067811865429 }, 3.141592653589793, 2.6794919243113386),
        (new double[] { -0.20486761065681386, -0.08972450585908759, 0.10993622685800519 }, new double[] { -0.2726154771995537, -0.1243829511348699, 0.10993622685800519 }, new double[] { -0.31346597025869344, -0.18858745617036146, 0.10993622685800519 },
         new double[] { -0.1750469204502661, -0.23155945333649633, 0.10993622685800519 }, new double[] { -0.20575083574078828, 0.9786044111856267, 0 }, new double[] { 0, 0, 1 }, 1.0625505626305893, 0.14493593719403822),
        (new double[] { -4.265091863517068, 1.476377952755902, 1.9685039370078687 }, new double[] { -4.5080956877504335, 1.4562420817953543, 1.9685039370078687 }, new double[] { -4.2650918635170765, -1.476377952755903, 1.9685039370078696 },
         new double[] { -4.265091863517067, -2.960594732333752e-16, 1.9685039370078692 }, new double[] { -6.015928496102195e-16, 1, -3.0079642480510977e-16 }, new double[] { -2.492470237609596e-17, 3.007964248051097e-16, 0.9999999999999999 }, 3.141592653589786, 1.476377952755902),
        (new double[] { -0.33555004303523206, 1.0127300754634698, 1.0318053530024107 }, new double[] { -0.39960421349605924, 0.9071699007580291, 1.0318053530024152 }, new double[] { -0.4527602366347251, 0.6675125013047957, 1.0318050549597353 },
         new double[] { 0.11451703609263195, 0.6674140779709226, 1.0318032223696898 }, new double[] { -0.7933811100655701, 0.6087252370133976, 3.7558929140362562e-06 }, new double[] { 3.2301613638823e-06, -1.960069720939354e-06, 0.9999999999928623 }, 0.6542793500061151, 0.5672772812686045),
        (new double[] { -4.229807343786891, 0.01055077769352037, 0.7500000000045997 }, new double[] { -4.240224010453557, 0.02859297360569612, 0.7500000000045999 }, new double[] { -4.271474010453557, 0.010550777693520308, 0.7500000000045997 },
         new double[] { -4.250640677120224, 0.010550777693520825, 0.7500000000045997 }, new double[] { 1, -2.1815882433884635e-14, -2.869329052689942e-28 }, new double[] { 1.8445686767292497e-29, -1.2306961192854837e-14, 1 }, 3.1415926535898397, 0.020833333333333037),
    };

    [Fact]
    public void Arc_of_three_points_is_the_arc_Reference_makes()
    {
        foreach (var c in Cases)
        {
            var arc = ThreePointArc.Create(c.P1, c.P2, c.P3);
            Assert.NotNull(arc);
            Assert.Equal(c.Center, arc!.Center);
            Assert.Equal(c.Ref, arc.Ref);
            Assert.Equal(c.Normal, arc.Normal);
            Assert.Equal(0.0, arc.Start);
            Assert.Equal(c.End, arc.End);
            Assert.Equal(c.Radius, arc.Radius);
        }
    }

    [Fact]
    public void Collinear_points_have_no_arc()
    {
        Assert.Null(ThreePointArc.Create(new double[] { 0.0, 0, 0 }, new double[] { 1.0, 0, 0 }, new double[] { 2.0, 0, 0 }));
    }
}
