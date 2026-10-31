using RvtIO.Geometry.Meshing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The removal of the clip vertices of the cylinder cell triangulation keeps every neighbour of a clip vertex, also the ones on one ray from it.</summary>
public class CylinderSeamTests
{
    [Fact]
    public void Collinear_neighbours_of_a_clip_vertex_all_stay_in_the_mesh()
    {
        // A cylinder cell triangulation whose clip vertex lies on the straight side of the far arc: the arc samples are collinear with it in the (axial, angle) plane.
        // Neighbours keyed by 2 pi - angle gave two upper samples the same key and one of them was lost (12 triangles instead of 13).
        var source = new[]
        {
            new[] { (3.9473684210526816, 4.39822971502571, false), (3.9473684210526803, 3.7699111843077535, false), (1.3157894736842592, 3.9269908169872423, false) },
            new[] { (3.9473684210526816, 4.39822971502571, false), (1.3157894736842592, 3.9269908169872423, false), (1.3157894736842592, 4.712388980384688, false) },
            new[] { (1.3157894736842592, 4.712388980384688, false), (3.947368421052682, 4.712388980384688, true), (3.9473684210526816, 4.39822971502571, false) },
            new[] { (3.9473684210526803, 3.7699111843077535, false), (3.947368421052682, 3.1415926535897896, false), (1.3157894736842592, 3.1415926535897896, false) },
            new[] { (3.9473684210526803, 3.7699111843077535, false), (1.3157894736842592, 3.1415926535897896, false), (1.3157894736842592, 3.9269908169872423, false) },
            new[] { (3.9473684210526803, 5.654866776461633, false), (3.9473684210526816, 5.0265482457436725, false), (3.947368421052682, 4.712388980384688, true) },
            new[] { (3.9473684210526803, 5.654866776461633, false), (3.947368421052682, 4.712388980384688, true), (2.7633833830853995, 6.053397300426155, false) },
            new[] { (2.7633833830853995, 6.053397300426155, false), (2.8947368421053605, 6.2831853071795845, false), (3.947368421052682, 6.283185307179586, false) },
            new[] { (2.7633833830853995, 6.053397300426155, false), (3.947368421052682, 6.283185307179586, false), (3.9473684210526803, 5.654866776461633, false) },
            new[] { (2.4997747950803677, 6.053397151001063, false), (2.7633833830853995, 6.053397300426155, false), (1.3157894736842592, 5.497787143782135, false) },
            new[] { (1.3157894736842592, 5.497787143782135, false), (1.3157894736842592, 6.283185307179586, false), (2.3684210526316747, 6.283185307179586, false) },
            new[] { (1.3157894736842592, 5.497787143782135, false), (2.3684210526316747, 6.283185307179586, false), (2.4997747950803677, 6.053397151001063, false) },
            new[] { (2.7633833830853995, 6.053397300426155, false), (3.947368421052682, 4.712388980384688, true), (1.3157894736842592, 4.712388980384688, false) },
            new[] { (2.7633833830853995, 6.053397300426155, false), (1.3157894736842592, 4.712388980384688, false), (1.3157894736842592, 5.497787143782135, false) }
        };
        var v = new List<CylinderMesher.VEntry>();
        var index = new Dictionary<(double, double, bool, int), int>();
        var t = new List<int[]>();
        foreach (var tri in source)
        {
            int cell = tri.Average(p => p.Item2) < 4.712388980384688 ? 0 : 1;
            var ids = new int[3];
            for (int i = 0; i < 3; i++)
            {
                var key = (tri[i].Item1, tri[i].Item2, tri[i].Item3, cell);
                if (!index.TryGetValue(key, out int id)) { id = v.Count; v.Add(new CylinderMesher.VEntry(tri[i].Item1, tri[i].Item2, tri[i].Item3, null)); index[key] = id; }
                ids[i] = id;
            }
            t.Add(ids);
        }
        const double radius = 0.06233595800524934;
        double[] Surface(double x, double y) => [x * radius, radius * Math.Cos(y), radius * Math.Sin(y)];
        var tree = KdTree.Build(Surface, 1.3157894736842592, 3.1415926535897896, 3.947368421052682, 6.283185307179586, 0.06002, 1.9232102321898465);
        var result = CylinderMesher.RemoveSeams(tree, v, t);
        Assert.Equal(13, result.Count);
        Assert.Contains(result.SelectMany(tri => tri), i => v[i].Y == 5.0265482457436725);
        Assert.DoesNotContain(result.SelectMany(tri => tri), i => v[i].Removable);
    }
}
