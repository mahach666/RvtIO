using RvtIO.Geometry.Meshing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The rings of one cell of a freeform face go to the profile triangulation together; a ring of two points has no area and is not one of them.</summary>
public class FreeformRingTests
{
    private static List<FfVtx> Square() => [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

    [Fact]
    public void A_polygon_alone_is_triangulated()
    {
        var tris = new List<(FfVtx, FfVtx, FfVtx)>();
        Assert.True(FreeformSolid.EmitRings(tris, [Square()], false, false, null));
        Assert.Equal(2, tris.Count);
    }

    [Fact]
    public void A_ring_of_two_points_beside_the_polygon_is_dropped_and_the_polygon_is_triangulated()
    {
        // A cell walk can leave the two samples of a loop on the cell border (u = 0) as a ring of their own; triangulating that pair must not fail.
        var line = new List<FfVtx> { new(0, 1), new(0, 0.5) };
        var tris = new List<(FfVtx, FfVtx, FfVtx)>();
        Assert.True(FreeformSolid.EmitRings(tris, [Square(), line], false, false, null));
        Assert.Equal(2, tris.Count);
    }

    [Fact]
    public void Rings_that_are_all_shorter_than_three_points_are_refused_not_an_empty_mesh()
    {
        var a = new List<FfVtx> { new(0, 1), new(0, 0.5) };
        var b = new List<FfVtx> { new(1, 1), new(1, 0.5) };
        var tris = new List<(FfVtx, FfVtx, FfVtx)>();
        Assert.False(FreeformSolid.EmitRings(tris, [a, b], false, false, null));
        Assert.Empty(tris);
    }

    [Fact]
    public void A_triangle_ring_is_a_ring_and_is_kept()
    {
        var outer = new List<FfVtx> { new(0, 0), new(4, 0), new(4, 4), new(0, 4) };
        var hole = new List<FfVtx> { new(1, 1), new(1, 2), new(2, 1) };
        var tris = new List<(FfVtx, FfVtx, FfVtx)>();
        Assert.True(FreeformSolid.EmitRings(tris, [outer, hole], false, false, null));
        Assert.True(tris.Count > 2);
    }
}
