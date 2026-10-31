using RvtIO.Parsing;
using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class WallPhysicalTableTests
{
    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void PrimaryFacesReplaceOwnReferenceNodesButKeepGeneratorColumnsAndArchiveIdentity(int cls)
    {
        var prefix = Prefix(1, 1589); var graph = Graph([([1], [])]);
        var value = Read(prefix, graph, (ushort)cls)!;
        Assert.Equal(1, value.GeometryCount); Assert.Equal("GRep", value.Slots[1].Node!.Archive);
        Assert.Equal("Wall", value.Visits[0].Previous!.Archive);
        Assert.Equal(value.Visits[0].Previous!.QueueIndex, value.Visits[0].Node!.QueueIndex); // equal queue numbers are not aliases
        Assert.Equal("write", value.Visits[0].Outcome);
        Assert.Equal(prefix.Slots.Select(s => s.Persisted), value.Slots.Select(s => s.Persisted));
    }

    [Fact]
    public void EmptySlotsAcceptNonPrimaryFacesButOccupiedSlotsDoNot()
    {
        var value = Read(Prefix(1, 1736), Graph([([1, 2], [])], allNested: true))!;
        Assert.Null(value.FirstDirectGeometryQueueIndex);
        Assert.Equal("Wall", value.Slots[1].Node!.Archive); Assert.Equal("GRep", value.Slots[2].Node!.Archive);
        Assert.Equal(new[] { "keep-non-primary", "write" }, value.Visits.Select(v => v.Outcome));
    }

    [Fact]
    public void LaterPrimaryFaceWinsIncludingRepeatedTagsAndOneLevelRegions()
    {
        var value = Read(Prefix(), Graph([([1, 1], [])], regions: [1]))!;
        Assert.Equal(3, value.Visits.Count);
        Assert.NotNull(value.Visits[1].RegionOfQueueIndex); Assert.Null(value.Visits[2].RegionOfQueueIndex);
        Assert.Equal(value.Visits[2].Node, value.Slots[1].Node);
        Assert.All(value.Visits, v => Assert.Equal("write", v.Outcome));
    }

    [Fact]
    public void SceneTraversalCanVisitNestedBodyBeforeSeparateFirstDirectBody()
    {
        var value = Read(Prefix(), Graph([([1], [2]), ([1], [2])], nestedBeforePrimary: true))!;
        Assert.Equal(2, value.GeometryCount);
        Assert.NotEqual(value.Visits[0].GeometryQueueIndex, value.FirstDirectGeometryQueueIndex);
        Assert.Equal(value.Visits[2].GeometryQueueIndex, value.FirstDirectGeometryQueueIndex);
        Assert.Equal(value.Visits[2].Node, value.Slots[1].Node);
        Assert.Equal(value.Visits[3].Node, value.Slots[2].Node);
    }

    [Fact]
    public void MultipleBodiesKeepNonPrimaryEdgeCollisionsEvenWhenOldNodeIsNotAnEdge()
    {
        var value = Read(Prefix(1, 1736), Graph([([], [1, 2]), ([], [1, 2])]))!;
        Assert.Equal(new[] { "write", "write", "keep-non-primary", "keep-non-primary" }, value.Visits.Select(v => v.Outcome));
        Assert.Equal(value.Visits[0].Node, value.Slots[1].Node); Assert.Equal(value.Visits[1].Node, value.Slots[2].Node);
    }

    [Fact]
    public void MultipleBodiesWithNoDirectPrimaryNeverReplaceOccupiedSlots()
    {
        var value = Read(Prefix(1, 1589), Graph([([], [1, 2]), ([], [1, 2])], allNested: true))!;
        Assert.Null(value.FirstDirectGeometryQueueIndex);
        Assert.Equal(new[] { "keep-non-primary", "write", "keep-non-primary", "keep-non-primary" }, value.Visits.Select(v => v.Outcome));
        Assert.Equal("Wall", value.Slots[1].Node!.Archive);
    }

    [Theory]
    [InlineData(1589)] [InlineData(1736)] [InlineData(1974)]
    public void SingleBodyEdgeOverwritesANonEdgeAndRetainsTheNativeWarningCondition(int oldClass)
    {
        var value = Read(Prefix(1, (ushort)oldClass), Graph([([], [1])]))!;
        Assert.Equal("write-single-non-edge", Assert.Single(value.Visits).Outcome);
        Assert.Equal(1243, value.Slots[1].Node!.ClassId);
    }

    [Fact]
    public void SingleBodyKeepsFirstEdgeButMultipleBodyPrimaryKeepsLastEdge()
    {
        var single = Read(Prefix(), Graph([([], [1, 1])]))!;
        Assert.Equal("keep-existing-edge", single.Visits[1].Outcome);
        Assert.Equal(single.Visits[0].Node, single.Slots[1].Node);
        var multiple = Read(Prefix(), Graph([([], [1, 1]), ([], [])]))!;
        Assert.Equal("write", multiple.Visits[1].Outcome);
        Assert.Equal(multiple.Visits[1].Node, multiple.Slots[1].Node);
    }

    [Theory]
    [InlineData(-1)] [InlineData(4)] [InlineData(int.MinValue)] [InlineData(int.MaxValue)]
    public void NegativeAndAboveLogicalMaxFaceAndEdgeTagsAreRetainedWithoutWrites(int tag)
    {
        var value = Read(Prefix(), Graph([([tag], [tag])]))!;
        Assert.All(value.Visits, v => { Assert.Equal(tag, v.Node!.Tag); Assert.Equal("out-of-range", v.Outcome); });
        Assert.All(value.Slots, s => Assert.Null(s.Node));
    }

    [Theory]
    [InlineData(0u)] [InlineData(0x80000u)] [InlineData(uint.MaxValue)]
    public void NodeFlagsAreNotAnActiveFaceOrEdgeFilter(uint flags)
    {
        var value = Read(Prefix(), Graph([([1], [2])], flags: flags))!;
        Assert.All(value.Visits, v => Assert.Equal(flags, v.Node!.Flags));
        Assert.Equal(2, value.Slots.Count(s => s.Node != null));
    }

    [Fact]
    public void NullAndWrongRuntimeKindEdgeSlotsAreNotCompactedOrInferredFromTags()
    {
        var value = Read(Prefix(), Graph([([], [null, 1])], edgeClass: 1254))!;
        Assert.Equal(new[] { 0, 1 }, value.Visits.Select(v => v.ArrayIndex));
        Assert.Equal(new[] { "null", "not-gedge" }, value.Visits.Select(v => v.Outcome));
        Assert.Null(value.Visits[0].SourceQueueIndex); Assert.NotNull(value.Visits[1].SourceQueueIndex);
        Assert.All(value.Slots, s => Assert.Null(s.Node)); // GEdgeLoop is NOT GEdge
    }

    [Fact]
    public void ZeroLocalBodiesRetainsOnlyTheNonBRepPrefix()
    {
        var prefix = Prefix(1, 1736); var value = Read(prefix, Graph([]))!;
        Assert.Equal(0, value.GeometryCount); Assert.Empty(value.Visits);
        Assert.Equal("Wall", value.Slots[1].Node!.Archive);
    }

    [Theory]
    [InlineData("owner")] [InlineData("wall-class")] [InlineData("bounds")]
    [InlineData("slot-id")] [InlineData("node-tag")]
    public void IncompatibleOwnerOrPrefixRejectsRatherThanClampingOrMatchingByShape(string mutation)
    {
        var prefix = Prefix(1, 1589); var graph = Graph([([1], [])]); ushort cls = 3524;
        if (mutation == "owner") graph = graph with { OwnerId = 94 };
        if (mutation == "wall-class") cls = 3905;
        if (mutation == "bounds") prefix = prefix with { MaxId = 2 };
        if (mutation is "slot-id" or "node-tag")
        {
            var slots = prefix.Slots.ToArray();
            slots[1] = mutation == "slot-id" ? slots[1] with { Persisted = slots[1].Persisted with { Id = 3 } }
                : slots[1] with { Node = slots[1].Node! with { Tag = 3 } };
            prefix = prefix with { Slots = slots };
        }
        Assert.Null(ApplyOwnWallPhysicalTablePrefix(cls, prefix, graph, out var failure)); Assert.Equal("physical-owner", failure);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MalformedGInfoRejectsExplicitlyWithoutOverflow(bool badFlags)
    {
        var graph = Graph([([1], [])]); var nodes = graph.Nodes.ToArray();
        var face = nodes[2]; var info = face.Fields[0];
        nodes[2] = face with { Fields = [info with { Items = [new(0, 0,
            badFlags ? [1, 0, -1, (long)uint.MaxValue + 1] : [(long)int.MaxValue + 1, 0, -1, 0], null, [])] }, ..face.Fields.Skip(1)] };
        Assert.Null(ApplyOwnWallPhysicalTablePrefix(3524, Prefix(), graph with { Nodes = nodes }, out var failure));
        Assert.StartsWith("physical-face:", failure);
    }

    private static OwnWallPhysicalTablePrefix? Read(OwnWallNonBRepTablePrefix prefix, OwnGElementGeometryGraph graph, ushort cls = 3524)
    {
        var value = ApplyOwnWallPhysicalTablePrefix(cls, prefix, graph, out var failure);
        Assert.Null(failure); return Assert.IsType<OwnWallPhysicalTablePrefix>(value);
    }
    private static OwnWallNonBRepTablePrefix Prefix(int? tag = null, ushort cls = 1589) => new(93, 3, [],
        Enumerable.Range(0, 4).Select(i => new WallNonBRepSlot(new(i, i == 1 ? -2 : int.MinValue + i, "Small", 0, i, 0, 4),
            i == tag ? new WallNonBRepNode(1, cls, i, uint.MaxValue, 0, 80) : null)).ToArray());

    private static OwnGElementGeometryGraph Graph((int[] Faces, int?[] Edges)[] bodies,
        bool allNested = false, bool nestedBeforePrimary = false, int[]? regions = null,
        uint flags = 4, ushort edgeClass = 1243)
    {
        var nodes = new List<GElementGraphNode> { N(-1, 2007, 93) };
        int? group = allNested || nestedBeforePrimary ? Add(2015, -1) : null;
        var geometryIndices = new List<int>();
        foreach (var body in bodies)
        {
            int geometry = Add(2092, -1); geometryIndices.Add(geometry);
            var faces = new List<GElementGraphPointer>(); var edges = new List<GElementGraphPointer>();
            foreach (int tag in body.Faces)
            {
                int face = Add(1589, tag); faces.Add(P(face, 1589));
                var faceRegions = new List<GElementGraphPointer>();
                if (faces.Count == 1) foreach (int regionTag in regions ?? [])
                { int region = Add(1589, regionTag); faceRegions.Add(P(region, 1589)); SetFace(region, []); }
                SetFace(face, faceRegions.ToArray());
            }
            foreach (int? tag in body.Edges) edges.Add(tag == null ? new(0, null, null, null, [], 0, 0) : P(Add(edgeClass, tag.Value), edgeClass));
            nodes[geometry + 1] = nodes[geometry + 1] with { Fields = [Info(-1), A(1978, "m_pFaces", faces.ToArray()), A(2092, "m_pEdges", edges.ToArray())] };
        }
        var pointers = geometryIndices.Select(i => P(i, 2092)).ToArray();
        if (group is int groupIndex)
        {
            nodes[groupIndex + 1] = nodes[groupIndex + 1] with { Fields = [Info(-1), A(2009, "m_subNodes", allNested ? pointers : pointers[..^1])] };
            pointers = allNested ? [P(groupIndex, 2015)] : [P(groupIndex, 2015), pointers[^1]];
        }
        nodes[0] = nodes[0] with { Fields = [Info(93), A(2009, "m_subNodes", pointers)] };
        return new(93, 0, nodes, [], 0);
        int Add(ushort cls, int tag) { int index = nodes.Count - 1; nodes.Add(N(index, cls, tag)); return index; }
        GElementGraphNode N(int index, ushort cls, int tag) => new(index, cls, 7, index * 100, index * 100 + 80, [Info(tag)]);
        GElementGraphField Info(int tag) => new(1246, "m_GInfo", null, 0, 16, [new(0, 16, [tag, 0, -1, flags], null, [])]);
        void SetFace(int index, GElementGraphPointer[] members) => nodes[index + 1] = nodes[index + 1] with
        { Fields = [nodes[index + 1].Fields[0], A(1590, "m_faceRegions", members), new(1589, "m_pSurf", null, 0, 4, [new(0, 4, [], new(0, null, null, null, [], 0, 4), [])])] };
        static GElementGraphPointer P(int index, ushort cls) => new(7, cls, index, null, [], 0, 6);
        static GElementGraphField A(int declaring, string name, params GElementGraphPointer[] pointers) =>
            new(declaring, name, (uint)pointers.Length, 0, 0, pointers.Select(p => new GElementGraphItem(0, 0, [], p, [])).ToArray());
    }
}
