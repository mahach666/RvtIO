using RvtIO.Parsing;
using Xunit;
using static RvtIO.Parsing.TfBlockDeserializer;

namespace RvtIO.Tests;

public class WallSide0Tests
{
    [Fact]
    public void SideIdUsesFormPointerOrderInsteadOfDeferredQueueOrder()
    {
        var input = History();
        Assert.Equal(6, SelectOwnWallSide0Id(input));
        var history = input.Histories.Histories;
        var phases = history.List.Phases.ToArray();
        phases[1] = phases[1] with { Members = phases[1].Members.AsEnumerable().Reverse().ToArray() };
        history = history with { List = history.List with { Phases = phases } };
        Assert.Equal(19, SelectOwnWallSide0Id(input with { Histories = input.Histories with { Histories = history } }));
    }

    [Theory]
    [InlineData("incomplete")] [InlineData("wrong-wall")] [InlineData("missing-step")]
    [InlineData("wrong-owner")] [InlineData("duplicate-history")] [InlineData("wrong-keys")]
    [InlineData("bad-count")] [InlineData("duplicate-table")]
    public void SideIdRejectsIncompleteOrAmbiguousHistory(string mutation)
    {
        var input = History();
        var history = input.Histories.Histories;
        var steps = history.Steps.ToArray();
        var step = steps[1];
        var table = step.Tables[0];
        switch (mutation)
        {
            case "incomplete": history = history with { Complete = false }; break;
            case "wrong-wall": history = history with { List = history.List with { ClassId = 361 } }; break;
            case "missing-step": steps[1] = step with { QueueIndex = 99 }; break;
            case "wrong-owner": steps[1] = step with { OwnerReference = 7 }; break;
            case "duplicate-history": steps[1] = step with { Tables = [table with { Count = 2, Entries = [.. table.Entries, .. table.Entries] }] }; break;
            case "wrong-keys": steps[1] = step with { Tables = [table with { Entries = [table.Entries[0] with { Keys = [2, -1, -1] }] }] }; break;
            case "bad-count": steps[1] = step with { Tables = [table with { Count = uint.MaxValue }] }; break;
            case "duplicate-table": steps[1] = step with { Tables = [table, table] }; break;
        }
        history = history with { Steps = steps };
        Assert.Null(SelectOwnWallSide0Id(input with { Histories = input.Histories with { Histories = history } }));
    }

    private static OwnWallCompleteInputs History()
    {
        var empty = new WallPhasePointer(0, null, null, 0, 0);
        var list = new OwnWallGeometryPhases(93, 3524, 0, empty, 0, 0, [],
            [new("phase0", 0, 0, 0, []), new("form", 2, 0, 0,
                [new(7, 596, 2, 0, 0), new(7, 596, 1, 0, 0)])], [], 2, [], 0, 0);
        WallGeometryStepPrefix Step(int index, int id) => new(index, 596, 7, 0, 0,
            [new("m_faceHistTable", 1, 0, 0, [new(id, [1, -1, -1], 0, 0)])], 0, 0, 0, 2);
        var history = new OwnWallGeometryHistories(list, [], [Step(1, 19), Step(2, 6)], 0, 0,
            true, null, [], [], [], [], empty, []);
        return new(new(history, [], [], []), [], [], 0);
    }

    [Fact]
    public void PlaneNormalComesFromOwnedUvAxesWithoutRenderedWindingOrFlip()
    {
        var actual = SelectOwnWallSide0Normal(Graph(), 6)!.Value;
        Assert.Equal(2u, actual.SourceFace);
        Assert.Equal(new double[] { -1, 0, 0 }, actual.Normal);
    }

    [Theory]
    [InlineData("wrong-tag")] [InlineData("duplicate-tag")] [InlineData("wrong-kind")]
    [InlineData("missing-link")] [InlineData("duplicate-link")] [InlineData("nonplane")]
    [InlineData("nonfinite")] [InlineData("degenerate")] [InlineData("missing-axis")]
    [InlineData("duplicate-axis")] [InlineData("not-local")]
    public void UnconfirmedNormalRejects(string mutation)
    {
        var graph = Graph();
        var nodes = graph.Nodes.ToArray();
        switch (mutation)
        {
            case "wrong-tag": nodes[2] = nodes[2] with { Fields = [Info(7), .. nodes[2].Fields.Skip(1)] }; break;
            case "duplicate-tag": nodes[0] = nodes[0] with { Fields = [Info(6), .. nodes[0].Fields.Skip(1)] }; break;
            case "wrong-kind": nodes[2] = nodes[2] with { ClassId = 1243 }; break;
            case "missing-link": graph = graph with { FaceSurfaces = [] }; break;
            case "duplicate-link": graph = graph with { FaceSurfaces = [new(1, 2), new(1, 2)] }; break;
            case "nonplane": nodes[3] = nodes[3] with { ClassId = 983 }; break;
            case "nonfinite": nodes[3] = nodes[3] with { Fields = [Vector("m_xVec", double.NaN, 0, 0), Vector("m_yVec", 0, 1, 0)] }; break;
            case "degenerate": nodes[3] = nodes[3] with { Fields = [Vector("m_xVec", 0, 1, 0), Vector("m_yVec", 0, 2, 0)] }; break;
            case "missing-axis": nodes[3] = nodes[3] with { Fields = [Vector("m_xVec", 0, 1, 0)] }; break;
            case "duplicate-axis": nodes[3] = nodes[3] with { Fields = [.. nodes[3].Fields, nodes[3].Fields[0]] }; break;
            case "not-local": nodes[1] = nodes[1] with { Fields = [Info(-2), Array(1978, "m_pFaces"), Array(2092, "m_pEdges")] }; break;
        }
        Assert.Null(SelectOwnWallSide0Normal(graph with { Nodes = nodes }, 6));
    }

    [Fact]
    public void FrameBindingIncludesHeaderPayloadAndFooterAndRejectsInvalidBounds()
    {
        byte[] frame = new byte[20];
        byte[] digest = System.Security.Cryptography.SHA256.HashData(frame);
        Assert.True(OwnPartitionParts.MatchesFrame(frame, 0, 4, digest));
        for (int index = 0; index < frame.Length; index++)
        {
            frame[index] ^= 1;
            Assert.False(OwnPartitionParts.MatchesFrame(frame, 0, 4, digest));
            frame[index] ^= 1;
        }
        Assert.False(OwnPartitionParts.MatchesFrame(frame, -1, 4, digest));
        Assert.False(OwnPartitionParts.MatchesFrame(frame, 1, 4, digest));
        Assert.False(OwnPartitionParts.MatchesFrame(frame, 0, uint.MaxValue, digest));
    }

    private static GElementGraphField Info(int tag) => new(1246, "m_GInfo", null, 0, 0,
        [new(0, 0, [tag, 0, 0, 0], null, [])]);
    private static GElementGraphPointer Ptr(int index, ushort cls) => new(7, cls, index, null, [], 0, 0);
    private static GElementGraphField Array(int declaring, string name, params GElementGraphPointer[] pointers)
        => new(declaring, name, (uint)pointers.Length, 0, 0,
            pointers.Select(pointer => new GElementGraphItem(0, 0, [], pointer, [])).ToArray());
    private static GElementGraphField Vector(string name, params double[] values) => new(522, name, null, 0, 0,
        values.Select(value => new GElementGraphItem(0, 0, [BitConverter.DoubleToInt64Bits(value)], null, [])).ToArray());
    private static OwnGElementGeometryGraph Graph() => new(93, 0,
    [
        new(-1, 2007, 93, 0, 0, [Info(93), Array(2009, "m_subNodes", Ptr(0, 2092))]),
        new(0, 2092, 7, 0, 0, [Info(-2), Array(1978, "m_pFaces", Ptr(1, 1589)), Array(2092, "m_pEdges")]),
        new(1, 1589, 7, 0, 0, [Info(6), Array(1590, "m_faceRegions"),
            new(1589, "m_pSurf", null, 0, 0, [new(0, 0, [], Ptr(2, 522), [])])]),
        new(2, 522, 7, 0, 0, [Vector("m_xVec", 0, 0, 2), Vector("m_yVec", 0, 3, 0)]),
    ], [new(1, 2)], 0);
}
