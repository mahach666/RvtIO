using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>A Geometry node without a polygon in the geometry (all its faces are display surfaces, or its polygons duplicate those of another node) is written by
/// The expected export as a triangle set like any other node: the node keeps that mark, and its graphics style and the material of its faces, when a symbol's nodes are placed.</summary>
public class GhostNodeTests
{
    [Fact]
    public void The_ghost_mark_and_the_material_survive_the_placement_of_a_symbol()
    {
        var node = new GeometryNode(385, 7, [], 3.5) { Ghost = true, Style = 12, RenderStyleId = 345 };
        var placed = node with { Node = 1385, Placement = [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4] };
        Assert.True(placed.Ghost);
        Assert.Equal((12, 345), (placed.Style, placed.RenderStyleId));
    }

    [Fact]
    public void An_ordinary_node_is_not_a_ghost_node()
    {
        var node = new GeometryNode(386, 8, [], 3.5);
        Assert.False(node.Ghost);
        Assert.Equal((0, 0), (node.Style, node.RenderStyleId));
    }
}
