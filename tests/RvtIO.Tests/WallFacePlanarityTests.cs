using RvtIO.Parsing;
using static RvtIO.Parsing.TfBlockDeserializer;
using Xunit;

namespace RvtIO.Tests;

public class WallFacePlanarityTests
{
    [Theory]
    [InlineData("Wall", 522, 1, true)] [InlineData("GRep", 522, 1, true)]
    [InlineData("Wall", 983, 2, false)] [InlineData("GRep", 983, 2, false)]
    public void UsesFactoryBoundSurfaceTypeAndPreservesQualifiedSource(string archive, int cls, int type, bool planar)
    {
        var f = Fixture(archive, (ushort)cls); var visit = Assert.Single(Assert.Single(Read(f).Generators).Faces);
        Assert.Equal(type, visit.SurfaceType); Assert.Equal(planar, visit.IsPlanar); Assert.Equal(archive, visit.Surface.Archive);
        Assert.Equal(2, visit.Surface.Pointer.QueueIndex); Assert.Equal(400, visit.Surface.Start); Assert.Equal(505, visit.Surface.End);
        Assert.Equal(f.Active.Generators[0].Visits[0].TableNode, visit.Face);
        Assert.Equal(f.Active.Generators[0].Visits[0].History, visit.History);
    }

    [Theory]
    [InlineData("Wall")] [InlineData("GRep")]
    public void NullSurfaceIsTypeZeroRatherThanUnknownOrPlane(string archive)
    {
        var f = Fixture(archive); f = SurfacePointer(f, new(0, null, null, 244, 248));
        var visit = Assert.Single(Assert.Single(Read(f).Generators).Faces);
        Assert.Equal(0, visit.SurfaceType); Assert.False(visit.IsPlanar); Assert.Null(visit.Surface.Start); Assert.Null(visit.Surface.End);
    }

    [Theory]
    [InlineData(1u)] [InlineData(42u)] [InlineData(uint.MaxValue)]
    public void OwnedQueueAndClassLinksDoNotRequireAMagicHandleValue(uint handle)
    {
        var f = Fixture(handle: handle); Assert.Equal(1, Assert.Single(Assert.Single(Read(f).Generators).Faces).SurfaceType);
    }

    [Theory]
    [InlineData("Wall")] [InlineData("GRep")]
    public void EqualQueueNumbersInTheOtherArchiveDoNotChooseItsSurface(string archive)
    {
        var f = Fixture(archive);
        if (archive == "Wall") f = f with { Graph = f.Graph with { Nodes = [f.Graph.Nodes[0], f.Graph.Nodes[1] with { ClassId = 983 }] } };
        else f = f with { Input = f.Input with { LaterObjects = [f.Input.LaterObjects[0] with { ClassId = 983 }] } };
        Assert.Equal(1, Assert.Single(Assert.Single(Read(f).Generators).Faces).SurfaceType);
    }

    [Fact]
    public void NoActiveFacesPreservesGeneratorMetadataWithoutResolvingUnusedSurfaces()
    {
        var f = Fixture(); var gen = f.Active.Generators[0];
        f = f with { Active = f.Active with { Generators = [gen with { Visits = [gen.Visits[0] with { Outcome = "inactive-flag" }] }] } };
        f = SurfacePointer(f, new(42, 523, 2, 244, 250));
        var value = Assert.Single(Read(f).Generators); Assert.Empty(value.Faces);
        Assert.Equal(gen.PhaseIndex, value.PhaseIndex); Assert.Equal(gen.StepQueueIndex, value.StepQueueIndex);
        Assert.Equal(gen.GeneratorId, value.GeneratorId); Assert.Equal(gen.Flags, value.Flags);
    }

    [Theory]
    [InlineData("Wall")] [InlineData("GRep")]
    public void RepeatedHistoryRowsAreNotDeduplicated(string archive)
    {
        var f = Fixture(archive); var gen = f.Active.Generators[0]; var row = gen.Visits[0];
        f = f with { Active = f.Active with { Generators = [gen with { Visits = [row, row with { RowIndex = 1, History = row.History with { Keys = [2, 0, 0] } }] }] } };
        Assert.Equal(2, Assert.Single(Read(f).Generators).Faces.Count);
    }

    [Theory]
    [InlineData("Wall", "class")] [InlineData("GRep", "class")]
    [InlineData("Wall", "queue")] [InlineData("GRep", "queue")]
    [InlineData("Wall", "external")] [InlineData("GRep", "external")]
    [InlineData("Wall", "handle")] [InlineData("GRep", "handle")]
    public void UnknownMissingExternalOrMismatchedSurfaceIsExplicitlyUnresolved(string archive, string mutation)
    {
        var f = Fixture(archive); var pointer = new WallPhasePointer(42, 522, 2, 244, 250);
        pointer = mutation switch { "class" => pointer with { ClassId = 523 }, "queue" => pointer with { QueueIndex = 3 },
            "external" => pointer with { QueueIndex = null, ClassId = null }, _ => pointer with { Handle = 43 } };
        f = SurfacePointer(f, pointer);
        Assert.Null(SelectOwnWallDebugOffFacePlanarity(f.Input, f.References, f.Graph, f.Active, out var failure));
        Assert.StartsWith("planarity-surface:", failure);
    }

    [Fact]
    public void HandSurfaceIsNotReinterpretedAsAnOwnedPlane()
    {
        var f = Fixture("GRep"); var node = f.Graph.Nodes[0]; var surface = node.Fields[1]; var item = surface.Items[0];
        node = node with { Fields = [node.Fields[0], surface with { Items = [item with { Pointer = item.Pointer! with { HandToken = 8 } }] }] };
        f = f with { Graph = f.Graph with { Nodes = [node, f.Graph.Nodes[1]] } };
        Assert.Null(SelectOwnWallDebugOffFacePlanarity(f.Input, f.References, f.Graph, f.Active, out var failure));
        Assert.StartsWith("planarity-face:", failure);
    }

    [Theory]
    [InlineData("Wall")] [InlineData("GRep")]
    public void QualifiedSourceFaceSpanMustMatchTheCurrentTableNode(string archive)
    {
        var f = Fixture(archive); var gen = f.Active.Generators[0]; var row = gen.Visits[0];
        f = f with { Active = f.Active with { Generators = [gen with { Visits = [row with { TableNode = row.TableNode! with { End = 249 } }] }] } };
        Assert.Null(SelectOwnWallDebugOffFacePlanarity(f.Input, f.References, f.Graph, f.Active, out var failure));
        Assert.StartsWith("planarity-face:", failure);
    }

    [Theory]
    [InlineData("owner")] [InlineData("graph-owner")] [InlineData("ref-owner")] [InlineData("class")]
    [InlineData("root")] [InlineData("incomplete")] [InlineData("bounds")]
    public void IncompatibleContextIsNotUsed(string mutation)
    {
        var f = Fixture(); var h = f.Input.Histories.Histories;
        f = mutation switch
        {
            "owner" => f with { Active = f.Active with { OwnerId = 101 } },
            "graph-owner" => f with { Graph = f.Graph with { OwnerId = 101 } },
            "ref-owner" => f with { References = f.References with { OwnerId = 101 } },
            "class" => f with { Active = f.Active with { ClassId = 361 } },
            "root" => f with { Input = f.Input with { Histories = f.Input.Histories with { Histories = h with { List = h.List with { OwnerReference = 3 } } } } },
            "incomplete" => f with { Input = f.Input with { Histories = f.Input.Histories with { Histories = h with { Complete = false } } } },
            _ => f with { Active = f.Active with { MaxId = -2 } }
        };
        Assert.Null(SelectOwnWallDebugOffFacePlanarity(f.Input, f.References, f.Graph, f.Active, out var failure));
        Assert.Equal("planarity-owner", failure);
    }

    [Theory]
    [InlineData(3524)] [InlineData(361)]
    public void BothOwnWallClassesAreAdmitted(int cls)
    {
        var f = Fixture(); var h = f.Input.Histories.Histories;
        f = f with { Active = f.Active with { ClassId = (ushort)cls }, References = f.References with { ClassId = (ushort)cls },
            Input = f.Input with { Histories = f.Input.Histories with { Histories = h with { List = h.List with { ClassId = (ushort)cls } } } } };
        Assert.Equal((ushort)cls, Read(f).ClassId);
    }

    private sealed record Inputs(OwnWallCompleteInputs Input, OwnWallReferenceGeometryInputs References,
        OwnGElementGeometryGraph Graph, OwnWallDebugOffActiveFaceHistories Active);
    private static OwnWallDebugOffFacePlanarity Read(Inputs f)
    {
        var value = SelectOwnWallDebugOffFacePlanarity(f.Input, f.References, f.Graph, f.Active, out var failure);
        Assert.Null(failure); return Assert.IsType<OwnWallDebugOffFacePlanarity>(value);
    }
    private static Inputs SurfacePointer(Inputs f, WallPhasePointer p)
    {
        if (f.Active.Generators[0].Visits[0].TableNode!.Archive == "Wall")
            return f with { References = f.References with { Faces = [f.References.Faces[0] with { Surface = p }] } };
        var node = f.Graph.Nodes[0]; var field = node.Fields[1]; var item = field.Items[0];
        var graphPointer = new GElementGraphPointer(p.Handle, p.ClassId, p.QueueIndex, null, [], p.Start, p.End);
        node = node with { Fields = [node.Fields[0], field with { Items = [item with { Pointer = graphPointer }] }] };
        return f with { Graph = f.Graph with { Nodes = [node, f.Graph.Nodes[1]] } };
    }
    private static Inputs Fixture(string archive = "Wall", ushort surfaceClass = 522, uint handle = 42)
    {
        var empty = new WallPhasePointer(0, null, null, 0, 4); var p = new WallPhasePointer(handle, surfaceClass, 2, 244, 250);
        var list = new OwnWallGeometryPhases(100, 3524, 14, empty, 14, 36, [], [], [], 2, [], 0, 0);
        var history = new OwnWallGeometryHistories(list, [], [], 1, 200, true, null, [], [], [], [], empty, []);
        var input = new OwnWallCompleteInputs(new(history, [], [], []), [new(2, surfaceClass, handle, 400, 505, [])], [], 505);
        var reference = new WallReferenceFaceInput(1, 10, 200, 250, 1, 0, 0, 1u << 19, empty,
            new("regions", 0, 0, 0, []), empty, empty, 0, 0, 0, p);
        var references = new OwnWallReferenceGeometryInputs(100, 3524, 14, empty, empty,
            new("faces", 0, 0, 0, []), empty, 2, [], [], [], [reference], 250);
        GElementGraphField[] fields = [new(1246, "m_GInfo", null, 200, 216,
            [new(200, 216, [1, 0, 0, 1u << 19], null, [])]),
            new(1589, "m_pSurf", null, 244, 250, [new(244, 250, [], new(handle, surfaceClass, 2, null, [], 244, 250), [])])];
        var graph = new OwnGElementGeometryGraph(100, 0, [new(1, 1589, 10, 200, 250, fields), new(2, surfaceClass, handle, 400, 505, [])], [], 505);
        var active = new OwnWallDebugOffActiveFaceHistories(100, 3524, 3,
            [new(2, 7, 500, 77, -1, [new(0, new(1, [1, -1, -1], 40, 56), new(archive, 1, 1589, 1, 1u << 19, 200, 250), "active")])]);
        return new(input, references, graph, active);
    }
}
