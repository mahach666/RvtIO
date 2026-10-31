using RvtIO.Parsing;
using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class GElementArchiveReferencesTests
{
    [Fact]
    public void SerializedKeysResolveForwardReferencesWithoutUsingQueueNumbersOrGInfoTags()
    {
        var graph = Graph([Node(10, 1243, 3, Field(1244, "m_pFace", 7, 0)), Node(20, 1589, 7)]);
        var value = Read(graph); Assert.True(value.Complete);
        Assert.Equal(new uint[] { 2, 3, 7 }, value.Registrations.Select(r => r.Key));
        var link = value.References[0]; Assert.Equal(7u, link.Key); Assert.Equal(20, link.TargetQueueIndex);
        Assert.Equal((ushort?)1589, link.TargetClassId); Assert.Equal("own-node", link.Outcome);
        Assert.Equal(100, link.Start); Assert.Equal(104, link.End); Assert.Equal("null", value.References[1].Outcome);
    }

    [Fact]
    public void RootKeyIsTwoNotTheOwnerIdOrRawRootHandle()
    {
        var value = Read(Graph([Node(4, 1254, 3, Field(1255, "m_pFace", 2))]));
        var root = value.Registrations[0]; Assert.Equal(2u, root.Key); Assert.Equal(1000u, root.RawHandle);
        var link = Assert.Single(value.References); Assert.Equal("own-root", link.Outcome);
        Assert.Equal(-1, link.TargetQueueIndex); Assert.Equal((ushort?)2007, link.TargetClassId);
    }

    [Fact]
    public void SelectedExternalDocumentIsExplicitAndNotAnOwnNode()
    {
        var nested = new GElementGraphField(2238, "m_cda", null, 100, 104,
            [new(100, 104, [], null, [Field(413, "m_pDoc", 1)])]);
        var value = Read(Graph([Node(1, 2238, 3, nested)])); var link = Assert.Single(value.References);
        Assert.Equal("external-document", link.Outcome); Assert.Null(link.TargetQueueIndex); Assert.Null(link.TargetClassId);
        Assert.True(value.Complete); Assert.Equal(1u, value.ExternalDocumentKey);
    }

    [Fact]
    public void FFFFFFFFOwnersAreUntrackedAndNeverBecomeAReferenceKey()
    {
        var graph = Graph([Node(10, 522, uint.MaxValue), Node(11, 983, uint.MaxValue),
            Node(12, 1254, 3, Field(1255, "m_pFace", uint.MaxValue))]);
        var value = Read(graph); Assert.Equal(2, value.Registrations.Count);
        var link = Assert.Single(value.References); Assert.Equal("missing-key", link.Outcome);
        Assert.False(value.Complete); Assert.Null(link.TargetQueueIndex);
    }

    [Theory]
    [InlineData(4u)] [InlineData(123456u)]
    public void MissingKeysAreRetainedAsUnresolvedNotConvertedToNull(uint key)
    {
        var value = Read(Graph([Node(3, 1254, 3, Field(1255, "m_next", key))]));
        Assert.False(value.Complete); Assert.Equal("missing-key", Assert.Single(value.References).Outcome);
    }

    [Fact]
    public void RepeatedKeysAndAllArraySlotsArePreserved()
    {
        var value = Read(Graph([Node(1, 1243, 3, Field(1244, "m_pFace", 7, 7),
            Field(1244, "m_next", 3, 0), Field(1244, "m_prev", 0, 3)), Node(2, 1589, 7)]));
        Assert.Equal(6, value.References.Count); Assert.Equal(new[] { 0, 1, 0, 1, 0, 1 }, value.References.Select(l => l.ItemIndex));
        Assert.Equal(value.References[0].TargetQueueIndex, value.References[1].TargetQueueIndex);
    }

    [Fact]
    public void EqualKeysInSeparateCapturesNeverShareTargets()
    {
        var one = Read(Graph([Node(1, 1254, 3, Field(1255, "m_pFace", 7)), Node(2, 1589, 7)]));
        var two = Read(Graph([Node(1, 1254, 3, Field(1255, "m_pFace", 7)), Node(8, 1589, 7)]));
        Assert.Equal(2, Assert.Single(one.References).TargetQueueIndex); Assert.Equal(8, Assert.Single(two.References).TargetQueueIndex);
    }

    [Fact]
    public void InlineElementIdsAreNotMistakenForWeakReferences()
    {
        var render = new GElementGraphField(1590, "m_renderStyleId", null, 100, 104,
            [new(100, 104, [], null, [Field(20, "m_id", 999)])]);
        Assert.Empty(Read(Graph([Node(3, 1589, 3, render)])).References);
    }

    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(2u)]
    public void ReservedOrNullOwnedKeysAreNotRegistered(uint key)
    {
        Assert.Null(SelectOwnGElementFreshArchiveReferences(Graph([Node(1, 1254, key)]), out var failure));
        Assert.Equal("archive-reference-registration", failure);
    }

    [Fact]
    public void DuplicateOwnedKeysAreNotGuessedAliasesOrFirstLastWinners()
    {
        Assert.Null(SelectOwnGElementFreshArchiveReferences(Graph([Node(1, 1254, 3), Node(2, 1589, 3)]), out var failure));
        Assert.Equal("archive-reference-registration", failure);
    }

    [Theory]
    [InlineData("owner")] [InlineData("root-class")] [InlineData("root-handle")]
    [InlineData("no-root")] [InlineData("duplicate-queue")]
    public void InvalidArchiveRootOrIdentityFailsExplicitly(string mutation)
    {
        var graph = Graph([Node(1, 1254, 3)]); var root = graph.Nodes[0];
        graph = mutation switch
        {
            "owner" => graph with { OwnerId = 0 }, "root-class" => graph with { Nodes = [root with { ClassId = 2008 }, graph.Nodes[1]] },
            "root-handle" => graph with { Nodes = [root with { Handle = 999 }, graph.Nodes[1]] },
            "no-root" => graph with { Nodes = [graph.Nodes[1]] }, _ => graph with { Nodes = [root, graph.Nodes[1], graph.Nodes[1]] }
        };
        Assert.Null(SelectOwnGElementFreshArchiveReferences(graph, out var failure)); Assert.Equal("archive-reference-graph", failure);
    }

    [Theory]
    [InlineData("span")] [InlineData("value")] [InlineData("extra-value")] [InlineData("pointer")] [InlineData("unknown-field")]
    public void InvalidWeakFieldShapeIsNotAccepted(string mutation)
    {
        var f = Field(1255, "m_pFace", 3); var item = f.Items[0];
        f = mutation switch
        {
            "span" => f with { Items = [item with { End = 108 }] },
            "value" => f with { Items = [item with { Values = [-1] }] },
            "extra-value" => f with { Items = [item with { Values = [3, 4] }] },
            "pointer" => f with { Items = [item with { Pointer = new(3, 1589, 1, null, [], 100, 104) }] },
            _ => f with { Name = "unknown" }
        };
        Assert.Null(SelectOwnGElementFreshArchiveReferences(Graph([Node(1, 1254, 3, f)]), out var failure));
        Assert.StartsWith("archive-reference-field:", failure);
    }

    private static OwnGElementFreshArchiveReferences Read(OwnGElementGeometryGraph graph)
    {
        var value = SelectOwnGElementFreshArchiveReferences(graph, out var failure);
        Assert.Null(failure); return Assert.IsType<OwnGElementFreshArchiveReferences>(value);
    }
    private static OwnGElementGeometryGraph Graph(GElementGraphNode[] nodes)
        => new(1000, 0, new[] { Node(-1, 2007, 1000) }.Concat(nodes).ToArray(), [], 500);
    private static GElementGraphNode Node(int q, ushort cls, uint handle, params GElementGraphField[] fields)
        => new(q, cls, handle, 100, 200, fields);
    private static GElementGraphField Field(int cls, string name, params uint[] keys)
        => new(cls, name, keys.Length == 2 ? 2u : null, 100, 100 + keys.Length * 4,
            keys.Select((k, i) => new GElementGraphItem(100 + i * 4, 104 + i * 4, [k], null, [])).ToArray());
}
