using RvtIO.Internal;
using RvtIO.Parsing;
using Xunit;

namespace RvtIO.Tests;

/// <summary>The trimmed location line of a straight wall: the line bounded by the extreme parameters of the vertices of the two side faces,
/// found in the first BaseWallGStep (the file keys them [side + 1, -1, -1]).</summary>
public class OwnWallTrimmedRangeTests
{
    private static TfBlockDeserializer.GElementGraphNode FaceNode(int queueIndex, int tag) => new(queueIndex, 1589, (uint)queueIndex + 1, 0, 0,
        [new(1246, "m_GInfo", null, 0, 0, [new(0, 0, [tag, 0, -1, 0], null, [])])]);

    private static TfBlockDeserializer.OwnGElementGeometryGraph Graph(params TfBlockDeserializer.GElementGraphNode[] nodes) => new(1, 0, nodes, [], 0);

    private static TfBlockDeserializer.WallHistoryEntry Entry(int id, params int[] keys) => new(id, keys, 0, 0);

    private static GRepFace Side(uint handle, params double[][] vertices) => new() { SourceFace = handle, Vertices = vertices.ToList() };

    private static CurveDriverData Line(double start, double end) =>
        new() { OriginPoint = [10, 0, 0], DirectionVector = [1, 0, 0], EndParams = [start, end] };

    private static readonly TfBlockDeserializer.WallHistoryEntry[] Table = [Entry(9, 1, -1, -1), Entry(8, 2, -1, -1), Entry(1077, 3, 0, -1)];

    [Fact]
    public void SideFaces_BoundTheLine_ByTheExtremeParametersOfTheirVertices()
    {
        // location line from x = 10 (parameter 0) to x = 20; the side faces run from x = 9.5 to x = 20.25 (a join extends the wall)
        var graph = Graph(FaceNode(19, 8), FaceNode(20, 9));
        var body = new GRepData { Faces = [Side(21, [9.5, 0.1, 0], [20, 0.1, 0], [20, 0.1, 9], [9.5, 0.1, 9]), Side(20, [10, -0.1, 0], [20.25, -0.1, 0])] };

        var range = TfBlockDeserializer.SelectOwnWallTrimmedRange(graph, Table, body, Line(0, 10));

        Assert.Equal(-0.5, range!.Value.Lo, 12);
        Assert.Equal(10.25, range.Value.Hi, 12);
    }

    [Fact]
    public void InnerLoopsOfASideFace_CountAsWell()
    {
        var graph = Graph(FaceNode(19, 8), FaceNode(20, 9));
        var first = Side(21, [10, 0, 0], [20, 0, 0]);
        first.InnerVertices = [[[21, 0, 1], [22, 0, 1]]];
        var body = new GRepData { Faces = [first, Side(20, [10, 0, 0], [20, 0, 0])] };

        Assert.Equal(12, TfBlockDeserializer.SelectOwnWallTrimmedRange(graph, Table, body, Line(0, 10))!.Value.Hi, 12);
    }

    [Fact]
    public void MaximumNotAboveZero_FallsBackToTheEndParameterOfTheLine()
    {
        // The maximum starts at about zero: with every vertex at a negative parameter the end of the line stands in
        var graph = Graph(FaceNode(19, 8), FaceNode(20, 9));
        var body = new GRepData { Faces = [Side(21, [6, 0, 0], [8, 0, 0]), Side(20, [6, 0, 0], [8.5, 0, 0])] };

        var range = TfBlockDeserializer.SelectOwnWallTrimmedRange(graph, Table, body, Line(-4, -1.5));

        Assert.Equal(-4, range!.Value.Lo, 12);
        Assert.Equal(-1.5, range.Value.Hi, 12);
    }

    [Fact]
    public void SideFaceThatIsMissing_OrHasNoVertices_GivesNoRange()
    {
        var body = new GRepData { Faces = [Side(20, [10, 0, 0], [20, 0, 0])] };

        Assert.Null(TfBlockDeserializer.SelectOwnWallTrimmedRange(Graph(FaceNode(19, 8), FaceNode(20, 9)), Table, body, Line(0, 10)));
        Assert.Null(TfBlockDeserializer.SelectOwnWallTrimmedRange(Graph(FaceNode(20, 9)), Table, body, Line(0, 10)));
        Assert.Null(TfBlockDeserializer.SelectOwnWallTrimmedRange(Graph(FaceNode(19, 8), FaceNode(20, 9)),
            [Entry(9, 1, -1, -1)], body, Line(0, 10)));
    }

    [Fact]
    public void TagSharedWithAnotherNode_IsNotAFace()
    {
        var group = new TfBlockDeserializer.GElementGraphNode(4, 2009, 5, 0, 0,
            [new(1246, "m_GInfo", null, 0, 0, [new(0, 0, [9, 0, 24925, 0], null, [])])]);
        var body = new GRepData { Faces = [Side(21, [10, 0, 0], [20, 0, 0]), Side(20, [10, 0, 0], [20, 0, 0])] };

        Assert.Null(TfBlockDeserializer.SelectOwnWallTrimmedRange(Graph(FaceNode(19, 8), FaceNode(20, 9), group), Table, body, Line(0, 10)));
    }
}
