using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The vertex slots of an edge loop: the first request of a slot decides the
/// vertex, which is offered to the neighbouring slots of the loops once and only when their own evaluation is within 1e-6.</summary>
public class VertexShareTests
{
    private static readonly double[] First = { 1.0, 2.0, 3.0 };
    private static readonly double[] Second = { 1.0 + 1e-15, 2.0, 3.0 };
    private static readonly double[] Third = { 1.0 - 1e-15, 2.0, 3.0 };

    private static IReadOnlyDictionary<(uint, int), List<(uint, int)>> Triangle() => new Dictionary<(uint, int), List<(uint, int)>>
    {
        [(1, 1)] = new() { (2, 0), (3, 1) },
        [(2, 0)] = new() { (1, 1), (3, 1) },
        [(3, 1)] = new() { (1, 1), (2, 0) },
    };

    private static double[]? Own(uint edge, int end) => (edge, end) switch
    {
        (1, 1) => First,
        (2, 0) => Second,
        (3, 1) => Third,
        _ => new double[] { edge * 10.0, end, 0 },
    };

    [Fact]
    public void First_requested_slot_decides_the_vertex_of_its_neighbours()
    {
        var slots = GRepParserV2.MeshShareVertices(new uint[] { 1, 2, 3 }, Own, Triangle());
        Assert.Same(First, slots[(1, 1)]);
        Assert.Same(First, slots[(2, 0)]);
        Assert.Same(First, slots[(3, 1)]);

        var other = GRepParserV2.MeshShareVertices(new uint[] { 2, 1, 3 }, Own, Triangle());
        Assert.Same(Second, other[(1, 1)]);
        Assert.Same(Second, other[(2, 0)]);
        Assert.Same(Second, other[(3, 1)]);
    }

    [Fact]
    public void Neighbour_farther_than_the_tolerance_keeps_its_own_point()
    {
        double[] far = { 1.0 + 1e-5, 2.0, 3.0 };
        var slots = GRepParserV2.MeshShareVertices(new uint[] { 1, 2, 3 }, (e, n) => (e, n) == (3, 1) ? far : Own(e, n), Triangle());
        Assert.Same(First, slots[(2, 0)]);
        Assert.Same(far, slots[(3, 1)]);
    }

    [Fact]
    public void Offered_vertex_is_not_offered_on()
    {
        var chain = new Dictionary<(uint, int), List<(uint, int)>>
        {
            [(1, 1)] = new() { (2, 0) },
            [(2, 0)] = new() { (1, 1), (3, 0) },
            [(3, 0)] = new() { (2, 0) },
        };
        double[] a = { 0, 0, 0 }, b = { 1e-7, 0, 0 }, c = { 2e-7, 0, 0 };
        var slots = GRepParserV2.MeshShareVertices(new uint[] { 1, 2, 3 }, (e, n) => (e, n) switch { (1, 1) => a, (2, 0) => b, (3, 0) => c, _ => null }, chain);
        Assert.Same(a, slots[(1, 1)]);
        Assert.Same(a, slots[(2, 0)]);
        Assert.Same(c, slots[(3, 0)]);
    }

    [Fact]
    public void Slot_without_a_point_stays_empty()
    {
        var slots = GRepParserV2.MeshShareVertices(new uint[] { 1 }, (e, n) => null, Triangle());
        Assert.Empty(slots);
    }
}

/// <summary>The offers to neighbouring slots follow the links of the loops and the flipped bits of the Edges, not the distance of the points:
/// the ring of a circle in two halves has two corners, and a proximity match finds the same one twice.</summary>
public class ConnectedSlotsTests
{
    // a ring of two Edges on the Face 10 (the other side: 20 and 30, loops that are not part of the test), the sentinel of the loop is 99
    private static (Dictionary<uint, GRepParserV2.EdgeRec?> Edges, Dictionary<uint, (uint First, uint Last, uint Face)> Loops) Ring(byte flags2 = 6)
    {
        var e1 = new GRepParserV2.EdgeRec { Handle = 1, FaceA = 10, FaceB = 20, Flags = 6, NextA = 2, PrevA = 99, NextB = 98, PrevB = 98 };
        var e2 = new GRepParserV2.EdgeRec { Handle = 2, FaceA = 10, FaceB = 30, Flags = flags2, NextA = 99, PrevA = 1, NextB = 97, PrevB = 97 };
        return (new() { [1] = e1, [2] = e2 }, new() { [99] = (1, 2, 10) });
    }

    [Fact]
    public void BothCornersOfARingOfTwoEdgesAreLinked()
    {
        var (edges, loops) = Ring();
        // the end of the first Edge meets the start of the second, and the start of the first the end of the second (through the sentinel)
        Assert.Contains((2u, 0), GRepParserV2.MeshConnectedSlots(edges[1]!, 1, edges, loops));
        Assert.Contains((2u, 1), GRepParserV2.MeshConnectedSlots(edges[1]!, 0, edges, loops));
        Assert.Contains((1u, 0), GRepParserV2.MeshConnectedSlots(edges[2]!, 1, edges, loops));
        Assert.Contains((1u, 1), GRepParserV2.MeshConnectedSlots(edges[2]!, 0, edges, loops));
    }

    [Fact]
    public void AFlippedNeighbourIsAddressedByItsOtherEnd()
    {
        var (edges, loops) = Ring(flags2: 7);
        Assert.Contains((2u, 1), GRepParserV2.MeshConnectedSlots(edges[1]!, 1, edges, loops));
        Assert.Contains((2u, 0), GRepParserV2.MeshConnectedSlots(edges[1]!, 0, edges, loops));
        // the flipped Edge itself asks the other way round: its first end is the one that meets the following Edge
        Assert.Contains((1u, 0), GRepParserV2.MeshConnectedSlots(edges[2]!, 0, edges, loops));
    }
}
